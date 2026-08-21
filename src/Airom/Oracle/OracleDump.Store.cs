// AIrom's side of the oracle's store mode.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Oracle;

public static partial class OracleDump
{
    /// <summary>
    /// The scripts a visit runs, typed exactly as a player would type them: a
    /// command, a letter, then offers ending in a return.
    ///
    /// An offer is only worth making if it is near the price, and the price
    /// depends on the item, the shopkeeper and the player's charisma - so the
    /// numbers are written as markers and worked out at the last moment. Each
    /// side works them out with its own arithmetic, so a disagreement about
    /// what something is worth shows up as two different scripts and a very
    /// loud diff.
    ///
    /// <list type="bullet">
    /// <item>%A - what the shopkeeper opens at, buying</item>
    /// <item>%B - the most the player would pay, where their haggling starts</item>
    /// <item>%C - half way between the two, and %D half way again</item>
    /// <item>%O - what the shopkeeper opens at, selling</item>
    /// <item>%M - the most the player could hope to be paid</item>
    /// <item>%P - half way between those two</item>
    /// <item>%L - the letter of the first pack slot this shop will look at</item>
    /// </list>
    ///
    /// Padding with escapes is safe - an escape backs out of whatever is being
    /// asked - so a script that runs out part-way ends the visit rather than
    /// spinning.
    /// </summary>
    private static readonly string[] StoreScripts =
    [
        "",                                  // walk in and walk out
        "b",                                 // turn the page
        "bb",                                // and turn it back
        "pa%A\r",                            // buy at the asking price
        "pa%C\r%A\r",                        // offer the middle, then the asking
        "pa%B\r%C\r%D\r%A\r",                // three rounds, then take it
        "pa%B\r+1\r\r\r\r\r\r\r\r",          // haggling upwards by increments
        "pa1\r",                             // an offer that is an insult
        "pa1\rpa1\rpa1\rpa1\rpa1\rpa1\r",    // insults until thrown out
        "s%L%O\r",                           // sell at what is offered
        "s%L%P\r%O\r",                       // ask the middle, then take it
        "s%L%M\r%P\r%O\r",                   // start high and come down
        "s%L%M\r-1\r\r\r\r\r\r\r\r",         // selling down by decrements
        "s%L99999\r",                        // an asking price out of all reason
        "s%L+0\r10\r",                       // an increment before any offer
        "i",                                 // the pack commands, from inside
        "z",                                 // a key that is no command at all
    ];

    /// <summary>
    /// Fills the markers in a script with the prices they stand for, worked out
    /// the way the shop itself would work them out.
    /// </summary>
    private static string FillScript(GameState game, string script, int storeIndex)
    {
        Store store = game.Stores.All[storeIndex];
        OwnerType owner = GameTables.Owners[store.Owner];

        int buyAsk = 1;
        int buyFloor = 1;
        int buyMiddle = 1;
        int buyNear = 1;

        if (store.StockCount > 0)
        {
            var wanted = new InvenType();
            Inventory.TakeOne(wanted, store.Stock[0].Item);

            int cost = game.Stores.SellPrice(storeIndex, wanted, out int maxSell, out _);

            buyAsk = Math.Max(1, maxSell * Stats.CharismaAdjust(game.Player) / 100);
            buyFloor = Math.Max(1, cost * (200 - owner.MaxInflate) / 100);
            buyMiddle = (buyFloor + buyAsk) / 2;
            buyNear = (buyMiddle + buyAsk) / 2;
        }

        int sellSlot = -1;

        for (int i = 0; i < game.Inventory.Count; i++)
        {
            if (StoreSets.Buys(storeIndex, game.Inventory[i].TVal))
            {
                sellSlot = i;
                break;
            }
        }

        int sellOpen = 1;
        int sellHope = 1;
        int sellMiddle = 1;

        if (sellSlot >= 0)
        {
            var offered = new InvenType();
            Inventory.TakeOne(offered, game.Inventory[sellSlot]);

            int cost = Math.Max(1, game.Stores.ItemValue(offered));

            cost = cost * (200 - Stats.CharismaAdjust(game.Player)) / 100;
            cost = cost
                * (200 - GameTables.RaceGoldAdjust[owner.OwnerRace][game.PlayerRace])
                / 100;

            cost = Math.Max(cost, 1);

            sellHope = cost * owner.MaxInflate / 100;
            sellOpen = Math.Max(1, cost * (200 - owner.MaxInflate) / 100);

            if (sellHope < sellOpen)
            {
                sellHope = sellOpen;
            }

            sellMiddle = (sellOpen + sellHope) / 2;
        }

        var filled = new System.Text.StringBuilder();

        for (int i = 0; i < script.Length; i++)
        {
            if (script[i] != '%' || i + 1 >= script.Length)
            {
                filled.Append(script[i]);
                continue;
            }

            char marker = script[i + 1];

            if (marker == 'L')
            {
                // A letter rather than a number: which pack slot to offer.
                filled.Append((char)('a' + Math.Max(sellSlot, 0)));
                i++;
                continue;
            }

            int? value = marker switch
            {
                'A' => buyAsk,
                'B' => buyFloor,
                'C' => buyMiddle,
                'D' => buyNear,
                'O' => sellOpen,
                'M' => sellHope,
                'P' => sellMiddle,
                _ => null,
            };

            if (value is int number)
            {
                filled.Append(number.ToString(CultureInfo.InvariantCulture));
                i++;
            }
            else
            {
                filled.Append(script[i]);
            }
        }

        return filled.ToString();
    }

    /// <summary>
    /// A visit to a shop: the screen, the commands and the haggling.
    ///
    /// The stock is whatever setting up and two rounds of restocking produced,
    /// which the shops mode already compares, so what is new here is everything
    /// that happens once the player is inside.
    /// </summary>
    public static void DumpStore(TextWriter output, uint seed, int storeIndex, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "store", seed);
        output.Write("store " + storeIndex.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = 0;
        game.Player = new Player();
        game.Knowledge.Reset();

        // The doors are locked until the clock has started.
        game.Turn = 100;

        game.Stores.Initialise();
        game.Stores.Maintain();
        game.Stores.Maintain();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        var display = new Display(game, screen);

        // Generated with the screen in hand, so the panel is sized by the
        // arrival rather than by the harness afterwards.
        new DungeonGenerator(game, display).Generate();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

        var loop = new GameLoop(game, display);
        Player player = game.Player;

        player.Level = 20;
        player.ExperienceFactor = 100;
        player.Gold = 5000;
        player.Food = 5000;

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 18;
            player.CurrentStat[i] = 18;
            player.ModStat[i] = 0;
            loop.Stats.SetUseStat(i);
        }

        // The charisma is what every price is worked out from, so it is varied
        // across the scripts rather than pinned.
        player.UseStat[Stat.Charisma] = 3 + (variation % 16);

        game.Inventory.Reset();
        game.Inventory[Inventory.WieldSlot].CopyFrom(30);    // a stiletto
        game.Inventory[Inventory.LightSlot].CopyFrom(365);   // a wooden torch
        game.Inventory[Inventory.LightSlot].P1 = 5000;
        game.Inventory.EquipmentCount = 2;

        loop.Equipment.Recalculate();

        player.MaxHitPoints = 500;
        player.CurrentHitPoints = 500;

        // One of each of a spread of kinds, so whichever shop is visited has
        // something of the player's it is willing to look at.
        int[] kinds =
        [
            ItemCategory.Sword, ItemCategory.SoftArmor, ItemCategory.Potion1,
            ItemCategory.Scroll1, ItemCategory.Food, ItemCategory.Wand,
            ItemCategory.PrayerBook, ItemCategory.Digging, ItemCategory.Flask,
            ItemCategory.Amulet,
        ];

        foreach (int kind in kinds)
        {
            var held = new InvenType();
            held.CopyFrom(FirstOfCategory(kind));
            game.Inventory.Carry(held);
        }

        display.MessageWaitingFlag = false;
        loop.FreeTurn = false;

        // Filled in only now: the prices depend on the pack and the charisma,
        // both of which are only settled at this point.
        string script = FillScript(
            game, StoreScripts[variation % StoreScripts.Length], storeIndex);

        // Printed with the returns spelled out, so the line stays readable and
        // a diff points at the offer rather than at a carriage return.
        output.Write("script "
            + script.Replace("\r", "<cr>", StringComparison.Ordinal) + "\n");

        screen.SetKeys(script + new string((char)27, 2000 - script.Length));

        Action stopLogging = LogKeys(output, screen);
        loop.StoreScreen.Enter(storeIndex);
        stopLogging();

        Store store = game.Stores.All[storeIndex];

        string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        output.Write(string.Join(
            ' ', "gold", N(player.Gold),
            "packed", N(game.Inventory.Count),
            "weight", N(game.Inventory.Weight)) + "\n");

        output.Write(string.Join(
            ' ', "owner", N(store.Owner),
            "insults", N(store.InsultsThisVisit),
            "good", N(store.GoodBuys),
            "bad", N(store.BadBuys),
            "open", N(store.OpenAgainAt)) + "\n");

        output.Write("stock " + N(store.StockCount) + "\n");

        for (int j = 0; j < store.StockCount; j++)
        {
            InvenType item = store.Stock[j].Item;

            output.Write(string.Join(
                ' ', "  line", N(j),
                N(item.Index), N(item.TVal), N(item.SubVal), N(item.Number),
                N(item.Cost), N(store.Stock[j].Cost), N(item.P1),
                N(item.SpecialName), N(item.Identification)) + "\n");
        }

        for (int j = 0; j < game.Inventory.Count; j++)
        {
            output.Write("  pack " + N(j) + " " + N(game.Inventory[j].Number)
                + " " + game.Names.Describe(game.Inventory[j], withArticle: true)
                + "\n");
        }

        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write(
                "scr " + N(row) + " " + screen.GetRow(row).TrimEnd() + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }
}
