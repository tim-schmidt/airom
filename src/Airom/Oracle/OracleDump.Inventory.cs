// AIrom's side of the oracle's inven and getitem modes.
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
    /// One of each of these, the first of its kind in the object table, so the
    /// pack is the same every time and the letters do not move.
    /// </summary>
    private static readonly int[] InventoryKinds =
    [
        ItemCategory.Ring, ItemCategory.Amulet, ItemCategory.Shield,
        ItemCategory.Helm, ItemCategory.Boots, ItemCategory.Cloak,
        ItemCategory.Potion1, ItemCategory.Food, ItemCategory.Scroll1,
        ItemCategory.Wand,
    ];

    /// <summary>
    /// The scripts the inventory mode runs. The first character is the command
    /// the mode is entered with; the rest are keys fed to it.
    /// </summary>
    private static readonly string[] InventoryScripts =
    [
        "i",       // list the pack
        "e",       // list what is worn
        "?",       // the help screen
        "iew",     // both lists, then back out of wearing
        "x",       // swap the wielded weapon with the spare
        "wa",      // wear the first thing that can be worn
        "wA",      // the same, but confirmed first
        "wAy",     // the same, confirmed
        "ta",      // take the first worn thing off
        "da",      // drop the first thing in the pack
        "day",     // drop it, all of it
        "e d a",   // from the equipment list, throw something off
        "d/a",     // drop, swapped over to the equipment list
        "izz",     // two keys that are not commands at all
        "wz",      // a letter outside the range on offer
        "w*a",      // list what could be worn, then wear it
        "iwawa",    // the list up, then two things worn in a row
        "iwawa",    // the same, with two of the same ring carried
        "itata",    // two things taken off in a row
        "idaydayd", // dropped until there is no room left
    ];

    /// <summary>The scripts the item prompt runs.</summary>
    private static readonly string[] ItemPromptScripts =
    [
        "a",        // the first slot
        "c",        // the third
        "",   // backed out of
        "*a",       // listed first, then chosen
        "z",        // outside the range
        "A",        // a capital, which asks first
        "Ay",       // a capital, confirmed
        "/a",       // swapped to the equipment list
        "*/a",      // listed, then swapped, then chosen
        "2",        // picked by its inscription rather than its letter
    ];

    /// <summary>
    /// The inventory screens.
    ///
    /// The real lists and the real command mode run, and the whole screen is
    /// compared afterwards along with the pack, the equipment and the weight.
    /// The layout is the point: both lists are drawn as far right as the longest
    /// line allows, so one character more in one description moves the entire
    /// column.
    /// </summary>
    public static void DumpInventory(TextWriter output, uint seed, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "inven", seed);
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();

        game.InitSeeds(seed);
        game.Turn = 0;
        game.DungeonLevel = 5;
        game.Player = new Player { MaxDungeonLevel = 5 };
        game.Knowledge.Reset();

        string script = InventoryScripts[variation % InventoryScripts.Length];

        var screen = new MemoryScreen { TypeAheadVisible = false };
        string keys = script[1..];
        screen.SendKeys(keys + new string((char)27, 600 - keys.Length));

        var display = new Display(game, screen);

        new DungeonGenerator(game).Generate();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        var loop = new GameLoop(game, display);
        Player player = game.Player;

        player.Level = 20;
        player.ExperienceFactor = 100;
        player.HitDie = 10;
        player.Food = 5000;

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 18;
            player.CurrentStat[i] = 18;
            player.ModStat[i] = 0;
            loop.Stats.SetUseStat(i);
        }

        game.Inventory.Reset();

        game.Inventory[Inventory.WieldSlot].CopyFrom(30);        // a stiletto
        game.Inventory[Inventory.AuxiliarySlot].CopyFrom(34);    // a spare weapon
        game.Inventory[Inventory.BodySlot].CopyFrom(103);        // soft leather armor
        game.Inventory[Inventory.LightSlot].CopyFrom(365);       // a wooden torch
        game.Inventory[Inventory.LightSlot].P1 = 5000;
        game.Inventory.EquipmentCount = 4;

        loop.Equipment.Recalculate();

        player.MaxHitPoints = 500;
        player.CurrentHitPoints = 500;

        game.PlayerLight = true;
        display.Panel.Invalidate();
        loop.Lighting.CheckView();

        CarryOneOfEach(game);

        // A second ring for one variation, so that wearing has to take one out
        // of a pile rather than the whole of it.
        if (variation == 17)
        {
            var second = new InvenType();
            second.CopyFrom(FirstOfCategory(ItemCategory.Ring));
            game.Inventory.Carry(second);
        }

        // Something on the floor for one variation, so that dropping has to say
        // there is no room.
        if (variation == 12)
        {
            int index = game.Objects.Allocate();
            game.Objects[index].CopyFrom(30);
            game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = index;
        }

        display.MessageWaitingFlag = false;
        loop.FreeTurn = false;

        // Every other variation shows the weights, which narrows the room left
        // for the descriptions and so moves the whole column.
        game.ShowWeights = variation % 2 == 1;

        // Called again while it says it is not finished, as the main loop does:
        // the mode gives the world a turn and comes back.
        int rounds = 0;

        do
        {
            char command = rounds == 0
                ? script[0]
                : loop.InventoryScreen.ContinuingCommand ?? ' ';

            loop.FreeTurn = false;
            loop.InventoryScreen.Command(command);

            string N(int value) => value.ToString(CultureInfo.InvariantCulture);

            output.Write(string.Join(
                ' ', "round", N(rounds),
                "free", loop.FreeTurn ? "1" : "0",
                "doing", N(loop.InventoryScreen.ContinuingCommand ?? '\0')) + "\n");

            output.Write(string.Join(
                ' ', "  packed", N(game.Inventory.Count),
                "equipped", N(game.Inventory.EquipmentCount),
                "weight", N(game.Inventory.Weight)) + "\n");

            for (int i = 0; i < game.Inventory.Count; i++)
            {
                output.Write("  pack " + N(i) + " " + N(game.Inventory[i].Number)
                    + " " + game.Names.Describe(game.Inventory[i], withArticle: true)
                    + "\n");
            }

            for (int i = Inventory.WieldSlot; i < Inventory.Size; i++)
            {
                if (game.Inventory[i].TVal != ItemCategory.Nothing)
                {
                    output.Write("  worn " + N(i) + " "
                        + game.Names.Describe(game.Inventory[i], withArticle: true)
                        + "\n");
                }
            }

            output.Write("  floor "
                + N(game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex)
                + "\n");

            rounds++;
        }
        while (loop.InventoryScreen.ContinuingCommand is not null && rounds < 4);

        DumpScreenRows(output, screen, "scr");

        // The two lists on their own. The command mode puts the screen back
        // when it leaves, so the layout has to be drawn again to be compared.
        display.ClearScreen();
        output.Write("inven-col " + loop.InventoryScreen
            .ShowInventory(0, game.Inventory.Count - 1, game.ShowWeights, 50, null)
            .ToString(CultureInfo.InvariantCulture) + "\n");
        DumpScreenRows(output, screen, "list");

        display.ClearScreen();
        output.Write("equip-col " + loop.InventoryScreen
            .ShowEquipment(game.ShowWeights, 50)
            .ToString(CultureInfo.InvariantCulture) + "\n");
        DumpScreenRows(output, screen, "worn");

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>
    /// The prompt that asks which item, which every command that works on one
    /// goes through.
    /// </summary>
    public static void DumpItemPrompt(TextWriter output, uint seed, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "getitem", seed);
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();

        game.InitSeeds(seed);
        game.Turn = 0;
        game.DungeonLevel = 5;
        game.Player = new Player { MaxDungeonLevel = 5 };
        game.Knowledge.Reset();

        string script = ItemPromptScripts[variation % ItemPromptScripts.Length];

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(script + new string((char)27, 600 - script.Length));

        var display = new Display(game, screen);

        new DungeonGenerator(game).Generate();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        var loop = new GameLoop(game, display);
        Player player = game.Player;

        player.Level = 20;
        player.ExperienceFactor = 100;
        player.HitDie = 10;
        player.Food = 5000;

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 18;
            player.CurrentStat[i] = 18;
            player.ModStat[i] = 0;
            loop.Stats.SetUseStat(i);
        }

        game.Inventory.Reset();

        game.Inventory[Inventory.WieldSlot].CopyFrom(30);
        game.Inventory[Inventory.BodySlot].CopyFrom(103);
        game.Inventory[Inventory.LightSlot].CopyFrom(365);
        game.Inventory[Inventory.LightSlot].P1 = 5000;
        game.Inventory.EquipmentCount = 3;

        loop.Equipment.Recalculate();

        player.MaxHitPoints = 500;
        player.CurrentHitPoints = 500;

        game.PlayerLight = true;
        display.Panel.Invalidate();
        loop.Lighting.CheckView();

        CarryOneOfEach(game);

        // An inscription, so that a digit has something to find.
        game.Inventory[1].Inscription = "2";

        display.MessageWaitingFlag = false;
        loop.FreeTurn = false;

        int? chosen = loop.InventoryScreen.GetItem("Which one?", 0, Inventory.Size);

        output.Write(string.Join(
            ' ',
            "taken", chosen is null ? "0" : "1",
            "slot", (chosen ?? -1).ToString(CultureInfo.InvariantCulture),
            "free", loop.FreeTurn ? "1" : "0") + "\n");

        DumpScreenRows(output, screen, "scr");

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>The first object in the table of a kind.</summary>
    private static int FirstOfCategory(int category)
    {
        for (int i = 0; i < GameTables.ObjectList.Length; i++)
        {
            if (GameTables.ObjectList[i].TVal == category)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Puts one of each kind into the pack, in table order.</summary>
    private static void CarryOneOfEach(GameState game)
    {
        foreach (int kind in InventoryKinds)
        {
            for (int i = 0; i < GameTables.ObjectList.Length; i++)
            {
                if (GameTables.ObjectList[i].TVal == kind)
                {
                    var held = new InvenType();
                    held.CopyFrom(i);
                    game.Inventory.Carry(held);
                    break;
                }
            }
        }
    }

    private static void DumpScreenRows(TextWriter output, MemoryScreen screen, string label)
    {
        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write(
                label + " " + row.ToString(CultureInfo.InvariantCulture)
                + " " + screen.GetRow(row).TrimEnd() + "\n");
        }
    }
}
