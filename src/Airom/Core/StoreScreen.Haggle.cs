// Ported from the haggling and trading half of Umoria 5.6 source/store2.c -
// get_haggle, receive_offer, purchase_haggle, sell_haggle, store_purchase,
// store_sell and enter_store.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

public partial class StoreScreen
{
    /// <summary>How a round of haggling ended.</summary>
    private enum Deal
    {
        /// <summary>A price was agreed.</summary>
        Struck = 0,

        /// <summary>The player walked away, or the shopkeeper gave up on them.</summary>
        Refused = 1,

        /// <summary>One insult too many: the shop is shut.</summary>
        ThrownOut = 2,

        /// <summary>The shopkeeper will not touch it at any price.</summary>
        Worthless = 3,
    }

    /// <summary>
    /// Reads an offer. Mirrors get_haggle().
    ///
    /// A number is an offer. A number with a sign is a step from the last one,
    /// and it is remembered, so a bare return repeats the same step - which is
    /// what makes haggling bearable to type. Nothing may be offered as a step
    /// until there is a first offer to step from.
    /// </summary>
    /// <returns>False if the player gave up.</returns>
    private bool GetHaggle(string comment, ref int offer, int offersSoFar)
    {
        bool answered = true;
        bool increment = false;
        int length = comment.Length;
        int baseLength = length;

        if (offersSoFar == 0)
        {
            _lastIncrement = 0;
        }

        int typed = 0;

        do
        {
            _display.Print(comment, 0, 0);

            if (offersSoFar != 0 && _lastIncrement != 0)
            {
                string shown = "[" + (_lastIncrement < 0 ? '-' : '+')
                    + Math.Abs(_lastIncrement).ToString(CultureInfo.InvariantCulture)
                    + "] ";

                _display.Print(shown, 0, baseLength);
                length = baseLength + shown.Length;
            }

            if (!_display.GetString(0, length, 40, out string text))
            {
                answered = false;
            }

            string trimmed = text.TrimStart(' ');

            if (trimmed.StartsWith('+') || trimmed.StartsWith('-'))
            {
                increment = true;
            }

            if (offersSoFar != 0 && increment)
            {
                typed = ParseNumber(text);

                // A zero is not accepted here: it would never end the loop, and
                // it happens when nothing was typed after the sign at all.
                if (typed == 0)
                {
                    increment = false;
                }
                else
                {
                    _lastIncrement = typed;
                }
            }
            else if (offersSoFar != 0 && text.Length == 0)
            {
                typed = _lastIncrement;
                increment = true;
            }
            else
            {
                typed = ParseNumber(text);
            }

            if (answered && offersSoFar == 0 && increment)
            {
                _display.MessagePrint("You haven't even made your first offer yet!");
                typed = 0;
                increment = false;
            }
        }
        while (answered && typed == 0);

        if (!answered)
        {
            _display.EraseLine(0, 0);
            return false;
        }

        if (increment)
        {
            offer += typed;
        }
        else
        {
            offer = typed;
        }

        return true;
    }

    /// <summary>
    /// What atol() makes of what was typed: the leading number, or nothing at
    /// all when it does not start with one.
    /// </summary>
    private static int ParseNumber(string text)
    {
        int at = 0;

        while (at < text.Length && char.IsWhiteSpace(text[at]))
        {
            at++;
        }

        int start = at;

        if (at < text.Length && (text[at] == '+' || text[at] == '-'))
        {
            at++;
        }

        int digits = at;

        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            at++;
        }

        if (at == digits)
        {
            return 0;
        }

        return long.TryParse(
            text.AsSpan(start, at - start), CultureInfo.InvariantCulture, out long value)
            ? (int)value
            : 0;
    }

    /// <summary>
    /// Takes an offer, insisting that it moves in the right direction. Mirrors
    /// receive_offer().
    /// </summary>
    /// <param name="direction">
    /// 1 while buying, where an offer must not go down, and -1 while selling,
    /// where an asking price must not go up.
    /// </param>
    private Deal ReceiveOffer(
        int storeIndex, string comment, ref int offer, int lastOffer,
        int offersSoFar, int direction)
    {
        while (true)
        {
            if (!GetHaggle(comment, ref offer, offersSoFar))
            {
                return Deal.Refused;
            }

            if (offer * direction >= lastOffer * direction)
            {
                return Deal.Struck;
            }

            if (HaggleInsults(storeIndex))
            {
                return Deal.ThrownOut;
            }

            // Put back, so that stepping from the last offer still works.
            offer = lastOffer;
        }
    }

    /// <summary>
    /// Haggling over something the player wants to buy. Mirrors
    /// purchase_haggle().
    /// </summary>
    private Deal PurchaseHaggle(int storeIndex, InvenType item, out int price)
    {
        price = 0;

        Store store = Shops.All[storeIndex];
        OwnerType owner = GameTables.Owners[store.Owner];

        int cost = Shops.SellPrice(storeIndex, item, out int maxSell, out int minSell);

        maxSell = Math.Max(1, maxSell * Stats.CharismaAdjust(Player) / 100);
        minSell = Math.Max(1, minSell * Stats.CharismaAdjust(Player) / 100);

        // Signed on purpose, so that a mark-up over a hundred percent leaves a
        // ceiling below the cost rather than wrapping.
        int maxBuy = Math.Max(1, cost * (200 - owner.MaxInflate) / 100);

        int minStep = owner.HagglePercent;
        int maxStep = minStep * 3;

        HaggleCommands(selling: false);

        int asking = maxSell;
        int floor = minSell;
        int lastOffer = maxBuy;
        int offer = 0;
        int offersSoFar = 0;
        string comment = "Asking";
        int finalRounds = 0;
        bool skippedHaggling = false;

        // Straight to the floor for anyone who has dealt well here before.
        if (Shops.NoNeedToBargain(storeIndex, floor))
        {
            _display.MessagePrint(
                "After a long bargaining session, you agree upon the price.");

            asking = minSell;
            comment = "Final offer";
            skippedHaggling = true;

            // Set up so that a bare return accepts the price.
            _lastIncrement = minSell;
            offersSoFar = 1;
        }

        var outcome = Deal.Struck;
        bool settled = false;

        while (!settled)
        {
            bool keepAsking = true;

            while (!settled && keepAsking)
            {
                _display.PutBuffer(
                    comment + " :  " + asking.ToString(CultureInfo.InvariantCulture), 1, 0);

                outcome = ReceiveOffer(
                    storeIndex, "What do you offer? ", ref offer, lastOffer,
                    offersSoFar, 1);

                if (outcome != Deal.Struck)
                {
                    settled = true;
                }
                else if (offer > asking)
                {
                    SayMisheard();
                    offer = lastOffer;

                    // An automatic step that would overshoot is a mistake, and
                    // useless, so it is thrown away.
                    if (lastOffer + _lastIncrement > asking)
                    {
                        _lastIncrement = 0;
                    }
                }
                else if (offer == asking)
                {
                    settled = true;
                    price = offer;
                }
                else
                {
                    keepAsking = false;
                }
            }

            if (settled)
            {
                break;
            }

            int step = (offer - lastOffer) * 100 / (asking - lastOffer);

            if (step < minStep)
            {
                if (HaggleInsults(storeIndex))
                {
                    outcome = Deal.ThrownOut;
                    break;
                }
            }
            else if (step > maxStep)
            {
                step = Math.Max(step * 75 / 100, maxStep);
            }

            int give = ((asking - offer) * (step + Rng.RandInt(5) - 3) / 100) + 1;

            // The price never goes back up.
            asking -= Math.Max(give, 0);

            if (asking < floor)
            {
                asking = floor;
                comment = "Final Offer";

                // Set so that a bare return offers exactly the floor.
                _lastIncrement = floor - offer;
                finalRounds++;

                if (finalRounds > 3)
                {
                    outcome = IncreaseInsults(storeIndex) ? Deal.ThrownOut : Deal.Refused;
                    break;
                }
            }
            else if (offer >= asking)
            {
                price = offer;
                break;
            }

            lastOffer = offer;
            offersSoFar++;
            _display.EraseLine(1, 0);
            _display.PutBuffer(
                "Your last offer : " + lastOffer.ToString(CultureInfo.InvariantCulture),
                1, 39);

            SayCounterAsk(lastOffer, asking, finalRounds > 0);

            // A step that would now overshoot is cut back to an exact match.
            if (asking - lastOffer < _lastIncrement)
            {
                _lastIncrement = asking - lastOffer;
            }
        }

        if (outcome == Deal.Struck && !skippedHaggling)
        {
            Shops.UpdateBargain(storeIndex, price, floor);
        }

        return outcome;
    }

    /// <summary>
    /// Haggling over something the player wants to sell. Mirrors sell_haggle().
    ///
    /// The same shape the other way round, with two things of its own: a
    /// shopkeeper who cannot afford it says so and offers what they have, and
    /// something worth nothing at all is refused outright.
    /// </summary>
    private Deal SellHaggle(int storeIndex, InvenType item, out int price)
    {
        price = 0;

        Store store = Shops.All[storeIndex];
        int cost = Shops.ItemValue(item);

        if (cost < 1)
        {
            return Deal.Worthless;
        }

        OwnerType owner = GameTables.Owners[store.Owner];

        cost = cost * (200 - Stats.CharismaAdjust(Player)) / 100;
        cost = cost * (200 - GameTables.RaceGoldAdjust[owner.OwnerRace][_game.PlayerRace])
            / 100;

        cost = Math.Max(cost, 1);

        int maxSell = cost * owner.MaxInflate / 100;
        int maxBuy = Math.Max(1, cost * (200 - owner.MaxInflate) / 100);
        int minBuy = Math.Max(1, cost * (200 - owner.MinInflate) / 100);

        if (minBuy < maxBuy)
        {
            minBuy = maxBuy;
        }

        int minStep = owner.HagglePercent;
        int maxStep = minStep * 3;
        int purse = owner.MaxCost;

        HaggleCommands(selling: true);

        int offersSoFar = 0;
        int finalRounds = 0;
        bool skippedHaggling = false;
        int asking;
        int ceiling;
        string comment;

        if (maxBuy > purse)
        {
            finalRounds = 1;
            comment = "Final Offer";

            // Nothing to step to, so a bare return offers nothing.
            _lastIncrement = 0;
            asking = purse;
            ceiling = purse;

            _display.MessagePrint(
                "I am sorry, but I have not the money to afford such a fine item.");

            skippedHaggling = true;
        }
        else
        {
            asking = maxBuy;
            ceiling = Math.Min(minBuy, purse);
            comment = "Offer";

            if (Shops.NoNeedToBargain(storeIndex, ceiling))
            {
                _display.MessagePrint(
                    "After a long bargaining session, you agree upon the price.");

                asking = ceiling;
                comment = "Final offer";
                skippedHaggling = true;

                _lastIncrement = ceiling;
                offersSoFar = 1;
            }
        }

        int lastOffer = maxSell;
        int offer = 0;
        asking = Math.Max(asking, 1);

        var outcome = Deal.Struck;
        bool settled = false;

        while (!settled)
        {
            bool keepAsking = true;

            while (!settled && keepAsking)
            {
                _display.PutBuffer(
                    comment + " :  " + asking.ToString(CultureInfo.InvariantCulture), 1, 0);

                outcome = ReceiveOffer(
                    storeIndex, "What price do you ask? ", ref offer, lastOffer,
                    offersSoFar, -1);

                if (outcome != Deal.Struck)
                {
                    settled = true;
                }
                else if (offer < asking)
                {
                    SayMisheard();
                    offer = lastOffer;

                    if (lastOffer + _lastIncrement < asking)
                    {
                        _lastIncrement = 0;
                    }
                }
                else if (offer == asking)
                {
                    settled = true;
                    price = offer;
                }
                else
                {
                    keepAsking = false;
                }
            }

            if (settled)
            {
                break;
            }

            int step = (lastOffer - offer) * 100 / (lastOffer - asking);

            if (step < minStep)
            {
                if (HaggleInsults(storeIndex))
                {
                    outcome = Deal.ThrownOut;
                    break;
                }
            }
            else if (step > maxStep)
            {
                step = Math.Max(step * 75 / 100, maxStep);
            }

            int give = ((offer - asking) * (step + Rng.RandInt(5) - 3) / 100) + 1;

            // The offer never goes back down.
            asking += Math.Max(give, 0);

            if (asking > ceiling)
            {
                asking = ceiling;
                comment = "Final Offer";
                _lastIncrement = ceiling - offer;
                finalRounds++;

                if (finalRounds > 3)
                {
                    outcome = IncreaseInsults(storeIndex) ? Deal.ThrownOut : Deal.Refused;
                    break;
                }
            }
            else if (offer <= asking)
            {
                price = offer;
                break;
            }

            lastOffer = offer;
            offersSoFar++;
            _display.EraseLine(1, 0);
            _display.PutBuffer(
                "Your last bid " + lastOffer.ToString(CultureInfo.InvariantCulture),
                1, 39);

            SayCounterOffer(asking, lastOffer, finalRounds > 0);

            if (asking - lastOffer > _lastIncrement)
            {
                _lastIncrement = asking - lastOffer;
            }
        }

        if (outcome == Deal.Struck && !skippedHaggling)
        {
            Shops.UpdateBargain(storeIndex, price, ceiling);
        }

        return outcome;
    }
}
