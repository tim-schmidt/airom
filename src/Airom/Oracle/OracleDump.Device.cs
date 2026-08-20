// AIrom's side of the oracle's scroll, wand and staff modes.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Oracle;

/// <summary>
/// A scroll reader that answers the questions a scroll asks.
///
/// The C harness answers them with scripted keys - "a" for the first pack slot,
/// "k" for the letter a scroll of genocide wants. This gives the same answers
/// without a prompt, so the two sides do the same thing to the same level.
/// </summary>
internal sealed class ScriptedScrolls(GameState game, Display display, GameLoop loop)
    : Scrolls(game, display, loop)
{
    private readonly Display _display = display;

    protected override int? ChooseItem(string prompt, int first, int last)
    {
        AskQuestion();
        return 0;
    }

    protected override char? ChooseSymbol(string prompt)
    {
        AskQuestion();
        return 'k';
    }

    /// <summary>
    /// What asking costs, whether or not the question is really put: both
    /// get_item() and get_com() write their prompt with prt(), and writing to
    /// the message line flushes whatever message was waiting on it. A scroll
    /// that announces itself before it asks therefore loses that announcement
    /// off the screen, which is why the message compared afterwards begins with
    /// the answer rather than the announcement.
    /// </summary>
    private void AskQuestion() => _display.MessagePrint(null);
}

public static partial class OracleDump
{
    /// <summary>
    /// Reading scrolls, aiming wands, using staffs.
    ///
    /// Each item in the table is used once by the same character on the same
    /// freshly generated level, and everything it did is compared: the player,
    /// the level, the monsters left standing, the messages, and the generator.
    ///
    /// These reach further than a potion does - a scroll can wall the player in,
    /// a wand can dissolve a corridor - so the level is dumped as a set of
    /// counts rather than square by square, with the object and monster totals
    /// beside it.
    /// </summary>
    public static void DumpDevice(
        TextWriter output, string mode, uint seed, int level, int first, int count)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(mode);

        (int wanted1, int wanted2) = mode switch
        {
            "scroll" => (ItemCategory.Scroll1, ItemCategory.Scroll2),
            "wand" => (ItemCategory.Wand, ItemCategory.Wand),
            _ => (ItemCategory.Staff, ItemCategory.Staff),
        };

        Header(output, mode, seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("first " + first.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("count " + count.ToString(CultureInfo.InvariantCulture) + "\n");

        // MagicInit shuffles the appearance tables where they stand, so it runs
        // once and only the generator is re-seeded for each item.
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();

        uint finalState = 0;

        for (int which = first;
             which < first + count && which < GameTables.ObjectList.Length;
             which++)
        {
            int category = GameTables.ObjectList[which].TVal;

            if (category != wanted1 && category != wanted2)
            {
                continue;
            }

            game.InitSeeds(seed);
            game.Turn = 0;
            game.DungeonLevel = level;
            game.Player = new Player();
            game.Player.MaxDungeonLevel = level;
            game.Knowledge.Reset();

            var screen = new MemoryScreen { TypeAheadVisible = false };

            // Enough keys to dismiss any -more- the item's messages raise. The
            // C harness pads its script the same way.
            screen.SendKeys(new string((char)27, 600));

            var display = new Display(game, screen);

            new DungeonGenerator(game).Generate();
            game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

            display.Panel.Resize(game.Cave.Height, game.Cave.Width);

            var loop = new GameLoop(game, display);
            loop.Scrolls = new ScriptedScrolls(game, display, loop);

            Player player = game.Player;

            // Someone who can work a device and survive what it wakes. No
            // experience to start with: the level is pinned at twenty, and any
            // experience worth a level would be spent gaining it before the
            // item was used.
            player.Level = 20;
            player.ExperienceFactor = 100;
            player.HitDie = 10;
            player.Save = 40;
            player.Food = 5000;

            for (int i = 0; i < Stat.Count; i++)
            {
                player.MaxStat[i] = 18;
                player.CurrentStat[i] = 18;
                player.ModStat[i] = 0;
                loop.Stats.SetUseStat(i);
            }

            // A weapon, a suit of armour and a light, so the enchanting and
            // cursing scrolls have something to work on and a scroll can be
            // read at all.
            game.Inventory.Reset();

            game.Inventory[Inventory.WieldSlot].CopyFrom(30);    // a stiletto
            game.Inventory[Inventory.BodySlot].CopyFrom(103);    // soft leather armor
            game.Inventory[Inventory.HeadSlot].CopyFrom(96);     // a hard leather cap
            game.Inventory[Inventory.LightSlot].CopyFrom(365);   // a wooden torch
            game.Inventory[Inventory.LightSlot].P1 = 5000;
            game.Inventory.EquipmentCount = 4;

            // Recalculating works out the hit points from the class and the
            // constitution, so the survivable totals are set after it rather
            // than before.
            loop.Equipment.Recalculate();

            player.MaxHitPoints = 500;
            player.CurrentHitPoints = 500;
            player.MaxMana = 50;
            player.CurrentMana = 50;

            game.PlayerLight = true;
            display.Panel.Invalidate();
            loop.Lighting.CheckView();

            var item = new InvenType();
            item.CopyFrom(which);

            // Charges, so a wand or a staff has something to spend.
            if (category is ItemCategory.Wand or ItemCategory.Staff)
            {
                item.P1 = 15;
            }

            game.Inventory.Carry(item);

            // Generating the level and lighting it can leave a message waiting,
            // and a waiting message turns the first message the item prints
            // into a -more- prompt.
            display.MessageWaitingFlag = false;
            loop.FreeTurn = false;
            loop.NewLevel = false;

            if (category == ItemCategory.Wand)
            {
                // East, as the C harness's scripted direction key.
                loop.Devices.Aim(0, 6);
            }
            else if (category == ItemCategory.Staff)
            {
                loop.Devices.Use(0);
            }
            else
            {
                loop.Scrolls.Read(0);
            }

            int lit = 0;
            int marked = 0;
            int walls = 0;

            for (int row = 0; row < game.Cave.Height; row++)
            {
                for (int column = 0; column < game.Cave.Width; column++)
                {
                    CaveSquare square = game.Cave[row, column];

                    if (square.PermanentLight || square.TemporaryLight)
                    {
                        lit++;
                    }

                    if (square.FieldMark)
                    {
                        marked++;
                    }

                    if (square.Feature >= CaveFeature.MinCaveWall)
                    {
                        walls++;
                    }
                }
            }

            string N(int value) => value.ToString(CultureInfo.InvariantCulture);

            InvenType weapon = game.Inventory[Inventory.WieldSlot];
            InvenType body = game.Inventory[Inventory.BodySlot];
            InvenType head = game.Inventory[Inventory.HeadSlot];

            output.Write(string.Join(
                ' ', "item", N(which), "tval", N(category), "flags",
                GameTables.ObjectList[which].Flags.ToString(CultureInfo.InvariantCulture))
                + "\n");

            output.Write(string.Join(
                ' ',
                "  chp", N(player.CurrentHitPoints),
                "mana", N(player.CurrentMana),
                "exp", N(player.Experience),
                "food", N(player.Food),
                "dlev", N(game.DungeonLevel),
                "newlev", loop.NewLevel ? "1" : "0",
                "free", loop.FreeTurn ? "1" : "0") + "\n");

            output.Write(string.Join(
                ' ',
                "  at", N(game.CharacterRow), N(game.CharacterColumn),
                "blind", N(player.Blind),
                "conf", N(player.Confused),
                "afraid", N(player.Afraid),
                "prot", N(player.ProtectionFromEvil),
                "recall", N(player.WordOfRecall)) + "\n");

            output.Write(string.Join(
                ' ',
                "  fast", N(player.Hasted),
                "slow", N(player.Slowed),
                "blessed", N(player.Blessed),
                "confmon", player.ConfusingTouch ? "1" : "0") + "\n");

            output.Write(string.Join(
                ' ',
                "  monsters", N(game.Monsters.Count - MonsterPool.FirstIndex),
                "objects", N(game.Objects.Count - ObjectPool.FirstIndex),
                "lit", N(lit),
                "marked", N(marked),
                "walls", N(walls)) + "\n");

            output.Write(string.Join(
                ' ',
                "  wield", N(weapon.ToHit), N(weapon.ToDam), N(weapon.ToAc),
                "body", N(body.ToAc), body.Flags != 0 ? "1" : "0",
                "head", N(head.ToAc), head.Flags != 0 ? "1" : "0") + "\n");

            var sample = new InvenType();
            sample.CopyFrom(which);

            output.Write(string.Join(
                ' ',
                "  known", game.Knowledge.IsKindKnown(sample) ? "1" : "0",
                "packed", N(game.Inventory.Count),
                "charges", N(game.Inventory[0].P1),
                "message", screen.GetRow(0).TrimEnd()) + "\n");

            output.Write("  state "
                + game.Rng.State.ToString(CultureInfo.InvariantCulture) + "\n");

            finalState = game.Rng.State;
        }

        Line(output, "final-state", finalState);
    }
}
