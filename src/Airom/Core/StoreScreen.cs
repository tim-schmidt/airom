// Ported from the display and prompting half of Umoria 5.6 source/store2.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Being in a shop: the screen, the commands, and the haggling.
///
/// A price is not a number here but a negotiation. The shopkeeper has an
/// asking price and a floor, the player has an offer and a ceiling, and each
/// round moves the asking price down by a share of the gap - but only if the
/// player's step towards it was big enough to be worth answering. Too small a
/// step is an insult, and enough insults get the player thrown out and the door
/// locked for a few thousand turns.
///
/// Two things soften it. A shopkeeper who has been dealt with fairly often
/// enough skips the haggling and names their floor at once - see
/// <see cref="Stores.NoNeedToBargain"/> - and an item marked with a fixed price
/// is not haggled over at all.
/// </summary>
public partial class StoreScreen
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public StoreScreen(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    private Player Player => _game.Player;

    private Rng Rng => _game.Rng;

    private Stores Shops => _game.Stores;

    /// <summary>How many lines of stock fit on the screen at once.</summary>
    private const int PageSize = 12;

    /// <summary>
    /// The last increment the player typed, so that a bare return repeats it.
    /// Umoria's last_store_inc, which is deliberately kept between visits.
    /// </summary>
    private int _lastIncrement;

    // ------------------------------------------------------------ the screen

    /// <summary>Draws the whole shop. Mirrors display_store().</summary>
    private void DisplayStore(int storeIndex, int top)
    {
        Store store = Shops.All[storeIndex];

        _display.ClearScreen();
        _display.PutBuffer(GameTables.Owners[store.Owner].Name, 3, 9);
        _display.PutBuffer("Item", 4, 3);

        // Not in the original, which never shows weights in a shop; here the
        // show-weights option covers this list too.
        if (_game.ShowWeights)
        {
            _display.PutBuffer("Weight", 4, 51);
        }

        _display.PutBuffer("Asking Price", 4, 60);

        PrintGold();
        DisplayCommands();
        DisplayInventory(storeIndex, top);
    }

    /// <summary>Mirrors display_commands().</summary>
    private void DisplayCommands()
    {
        _display.Print("You may:", 20, 0);
        _display.Print(
            " p) Purchase an item.           b) Browse store's inventory.", 21, 0);
        _display.Print(
            " s) Sell an item.               i/e/t/w/x) Inventory/Equipment Lists.",
            22, 0);
        _display.Print(
            "ESC) Exit from Building.        ^R) Redraw the screen.", 23, 0);
    }

    /// <summary>Mirrors haggle_commands().</summary>
    private void HaggleCommands(bool selling)
    {
        _display.Print(selling
            ? "Specify an asking-price in gold pieces."
            : "Specify an offer in gold pieces.", 21, 0);

        _display.Print("ESC) Quit Haggling.", 22, 0);
        _display.EraseLine(23, 0);
    }

    /// <summary>
    /// Lists a page of stock. Mirrors display_inventory().
    ///
    /// A pile of things that stack is described as one of them, since the price
    /// beside it is the price of one.
    /// </summary>
    private void DisplayInventory(int storeIndex, int start)
    {
        Store store = Shops.All[storeIndex];

        int line = start % PageSize;
        int stop = ((start / PageSize) + 1) * PageSize;

        if (stop > store.StockCount)
        {
            stop = store.StockCount;
        }

        while (start < stop)
        {
            InvenType item = store.Stock[start].Item;

            byte number = item.Number;

            if (item.SubVal >= ItemCategory.SingleStackMin
                && item.SubVal <= ItemCategory.SingleStackMax)
            {
                item.Number = 1;
            }

            string description = _game.Names.Describe(item, withArticle: true);
            int shown = item.Number;
            item.Number = number;

            // The weight column is not in the original; it appears only when
            // the show-weights option is on, and weighs what the description
            // describes, since the price beside it is priced the same way.
            if (_game.ShowWeights && description.Length > 45)
            {
                description = description[..45];
            }

            _display.Print((char)('a' + line) + ") " + description, line + 5, 0);

            if (_game.ShowWeights)
            {
                int total = item.Weight * shown;

                _display.Print(
                    (total / 10).ToString(CultureInfo.InvariantCulture).PadLeft(3)
                    + "." + (total % 10).ToString(CultureInfo.InvariantCulture)
                    + " lb", line + 5, 51);
            }

            _display.Print(DescribeCost(store.Stock[start].Cost), line + 5, 59);

            line++;
            start++;
        }

        // Whatever the last page left behind is rubbed out.
        for (int j = line; j < PageSize; j++)
        {
            _display.EraseLine(j + 5, 0);
        }

        if (store.StockCount > PageSize)
        {
            _display.PutBuffer("- cont. -", 17, 60);
        }
        else
        {
            _display.EraseLine(17, 60);
        }
    }

    /// <summary>Redraws one price. Mirrors display_cost().</summary>
    private void DisplayCost(int storeIndex, int slot) =>
        _display.Print(
            DescribeCost(Shops.All[storeIndex].Stock[slot].Cost),
            (slot % PageSize) + 5, 59);

    /// <summary>
    /// A price as it is shown. A negative cost is one still open to haggling,
    /// and what is shown is what the shopkeeper would open at; a positive one
    /// is fixed and says so.
    /// </summary>
    private string DescribeCost(int cost)
    {
        if (cost > 0)
        {
            return cost.ToString(CultureInfo.InvariantCulture).PadLeft(9) + " [Fixed]";
        }

        int asking = -cost * Stats.CharismaAdjust(Player) / 100;

        if (asking <= 0)
        {
            asking = 1;
        }

        return asking.ToString(CultureInfo.InvariantCulture).PadLeft(9);
    }

    /// <summary>Mirrors store_prt_gold().</summary>
    private void PrintGold() =>
        _display.Print(
            "Gold Remaining : " + Player.Gold.ToString(CultureInfo.InvariantCulture),
            18, 17);

    // ---------------------------------------------------------- the prompts

    /// <summary>
    /// Asks which of the listed items. Mirrors get_store_item().
    /// </summary>
    /// <returns>The line chosen, or nothing when the player backed out.</returns>
    private int? GetStoreItem(string prompt, int first, int last)
    {
        string header = "(Items " + (char)(first + 'a') + "-" + (char)(last + 'a')
            + ", ESC to exit) " + prompt;

        while (_display.GetCommand(header, out char command))
        {
            int chosen = command - 'a';

            if (chosen >= first && chosen <= last)
            {
                _display.EraseLine(Display.MessageLine, 0);
                return chosen;
            }

            _display.Bell();
        }

        _display.EraseLine(Display.MessageLine, 0);
        return null;
    }

    /// <summary>
    /// Notes an insult, and throws the player out if there have been too many.
    /// Mirrors increase_insults().
    /// </summary>
    /// <returns>Whether the shop is now closed.</returns>
    private bool IncreaseInsults(int storeIndex)
    {
        Store store = Shops.All[storeIndex];
        store.InsultsThisVisit++;

        if (store.InsultsThisVisit <= GameTables.Owners[store.Owner].InsultMax)
        {
            return false;
        }

        SayThrownOut();
        store.InsultsThisVisit = 0;
        store.BadBuys++;
        store.OpenAgainAt = _game.Turn + 2500 + Rng.RandInt(2500);
        return true;
    }

    /// <summary>Mirrors decrease_insults(): a deal struck mends some fences.</summary>
    private void DecreaseInsults(int storeIndex)
    {
        Store store = Shops.All[storeIndex];

        if (store.InsultsThisVisit != 0)
        {
            store.InsultsThisVisit--;
        }
    }

    /// <summary>
    /// An offer not worth answering. Mirrors haggle_insults().
    /// </summary>
    /// <returns>Whether the haggling is over because the shop has closed.</returns>
    private bool HaggleInsults(int storeIndex)
    {
        if (IncreaseInsults(storeIndex))
        {
            return true;
        }

        SayInsulted();

        // Flushed on its own, so the insult is not run together with the rest
        // of the haggling.
        _display.MessagePrint(null);
        return false;
    }

    // ----------------------------------------------------------- the patter

    /// <summary>Mirrors prt_comment1().</summary>
    private void SayAccepted() =>
        _display.MessagePrint(
            GameTables.ShopkeeperAccepts[Rng.RandInt(GameTables.ShopkeeperAccepts.Length) - 1]);

    /// <summary>Mirrors prt_comment2(): what they say to an offer of theirs.</summary>
    private void SayCounterAsk(int offer, int asking, bool final)
    {
        string comment = final
            ? GameTables.ShopkeeperFinalAsk[
                Rng.RandInt(GameTables.ShopkeeperFinalAsk.Length) - 1]
            : GameTables.ShopkeeperCounterAsk[
                Rng.RandInt(GameTables.ShopkeeperCounterAsk.Length) - 1];

        _display.MessagePrint(FillIn(comment, offer, asking));
    }

    /// <summary>Mirrors prt_comment3(): what they say to a price the player asks.</summary>
    private void SayCounterOffer(int offer, int asking, bool final)
    {
        string comment = final
            ? GameTables.ShopkeeperFinalOffer[
                Rng.RandInt(GameTables.ShopkeeperFinalOffer.Length) - 1]
            : GameTables.ShopkeeperCounterOffer[
                Rng.RandInt(GameTables.ShopkeeperCounterOffer.Length) - 1];

        _display.MessagePrint(FillIn(comment, offer, asking));
    }

    /// <summary>Mirrors prt_comment4(): both halves of being thrown out.</summary>
    private void SayThrownOut()
    {
        int which = Rng.RandInt(GameTables.ShopkeeperLosesTemper.Length) - 1;

        _display.MessagePrint(GameTables.ShopkeeperLosesTemper[which]);
        _display.MessagePrint(GameTables.ShopkeeperThrowsOut[which]);
    }

    /// <summary>Mirrors prt_comment5().</summary>
    private void SayInsulted() =>
        _display.MessagePrint(
            GameTables.ShopkeeperInsulted[
                Rng.RandInt(GameTables.ShopkeeperInsulted.Length) - 1]);

    /// <summary>Mirrors prt_comment6().</summary>
    private void SayMisheard() =>
        _display.MessagePrint(
            GameTables.ShopkeeperMishears[
                Rng.RandInt(GameTables.ShopkeeperMishears.Length) - 1]);

    /// <summary>
    /// Puts the two numbers into a comment. Mirrors the pair of insert_lnum()
    /// calls, which replace the first occurrence of each marker and leave the
    /// comment alone if it has none.
    /// </summary>
    private static string FillIn(string comment, int offer, int asking)
    {
        comment = ReplaceFirst(comment, "%A1", offer);
        return ReplaceFirst(comment, "%A2", asking);
    }

    private static string ReplaceFirst(string text, string marker, int number)
    {
        int at = text.IndexOf(marker, StringComparison.Ordinal);

        return at < 0
            ? text
            : text[..at] + number.ToString(CultureInfo.InvariantCulture)
                + text[(at + marker.Length)..];
    }
}
