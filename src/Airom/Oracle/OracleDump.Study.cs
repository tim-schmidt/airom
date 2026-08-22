// AIrom's side of the oracle's study mode: learning spells and prayers.
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
    /// One scripted arrangement of a character with spells left to learn.
    /// </summary>
    private readonly record struct StudyCase(
        bool Mage,
        int Level,
        int NewSpells,
        int Books,
        uint Known,
        int Blind,
        bool Lit,
        int Confused,
        string Keys);

    /// <summary>
    /// The arrangements, matching the C harness one for one.
    /// </summary>
    private static readonly StudyCase[] StudyCases =
    [
        // Picking the top of the list over and over: every pick shortens the
        // list by one, so every pick redraws it one row shorter.
        new(true, 40, 4, 1, 0, 0, true, 0, "aaaa"),

        // Picking from the bottom and the middle instead.
        new(true, 40, 3, 1, 0, 0, true, 0, "cba"),

        // Keys that are not on offer at all, then one that is.
        new(true, 40, 2, 1, 0, 0, true, 0, "z0a"),

        // Four books at once: thirty-one spells on offer, twenty-two shown.
        new(true, 40, 5, 4, 0, 0, true, 0, "aaaaa"),

        // The last row shown, then the row past it, which is refused.
        new(true, 40, 3, 4, 0, 0, true, 0, "vwa"),

        // A low level, so most of the book is still out of reach.
        new(true, 5, 2, 1, 0, 0, true, 0, "aa"),

        // No book: nothing is on offer and the picks are kept.
        new(true, 40, 3, 0, 0, 0, true, 0, "a"),

        // More picks than the book can satisfy.
        new(true, 1, 4, 1, 0, 0, true, 0, "aa"),

        // Nothing saved up to spend.
        new(true, 40, 0, 1, 0, 0, true, 0, "a"),

        // Blind, unlit and confused: three ways to be turned away.
        new(true, 40, 2, 1, 0, 10, true, 0, "a"),
        new(true, 40, 2, 1, 0, 0, false, 0, "a"),
        new(true, 40, 2, 1, 0, 0, true, 10, "a"),

        // Backing out with a pick still in hand.
        new(true, 40, 2, 1, 0, 0, true, 0, "\u001b"),

        // Some of the book already known, so those rows are not offered again.
        new(true, 40, 2, 1, 0x0000000Bu, 0, true, 0, "ab"),

        // A priest, who is told what they learned rather than asked.
        new(false, 40, 3, 0, 0, 0, true, 0, ""),
        new(false, 10, 5, 0, 0, 0, true, 0, ""),
    ];

    /// <summary>
    /// Learning spells: the "G" command, gain_spells().
    ///
    /// A mage is shown every spell they are entitled to and picks them one at a
    /// time, and the list is redrawn after each pick because the one just
    /// learned comes out of it. That redraw only exists between one key and the
    /// next - the command saves the screen going in and puts it back coming out
    /// - so the screen is dumped at every prompt rather than at the end. A row
    /// left standing from the longer list is invisible to any dump taken
    /// afterwards, which is exactly where that bug lived.
    ///
    /// A priest is not asked at all: the prayer is picked for them, so what is
    /// compared there is the draw, the message and the order they are learned
    /// in.
    /// </summary>
    public static void DumpStudy(TextWriter output, uint seed, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        StudyCase test = StudyCases[
            ((variation % StudyCases.Length) + StudyCases.Length) % StudyCases.Length];

        int bookCategory = test.Mage ? ItemCategory.MagicBook : ItemCategory.PrayerBook;
        int stat = test.Mage ? Stat.Intelligence : Stat.Wisdom;

        string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        Header(output, "study", seed);
        output.Write("variation " + N(variation) + "\n");
        output.Write(string.Join(
            ' ',
            "mage", test.Mage ? "1" : "0",
            "level", N(test.Level),
            "newspells", N(test.NewSpells),
            "books", N(test.Books),
            "known", test.Known.ToString(CultureInfo.InvariantCulture)) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();

        game.InitSeeds(seed);
        game.Turn = 0;
        game.DungeonLevel = 5;
        game.Player = new Player { Class = test.Mage ? 1 : 2 };
        game.Player.MaxDungeonLevel = 5;
        game.Knowledge.Reset();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(test.Keys + new string((char)27, 600 - test.Keys.Length));

        var display = new Display(game, screen);

        // Generated with the screen in hand, so the panel is sized by the
        // arrival rather than by the harness afterwards.
        new DungeonGenerator(game, display).Generate();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

        var loop = new GameLoop(game, display);

        Player player = game.Player;
        player.Level = test.Level;
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

        game.Inventory[Inventory.WieldSlot].CopyFrom(30);    // a stiletto
        game.Inventory[Inventory.BodySlot].CopyFrom(103);    // soft leather armor
        game.Inventory[Inventory.LightSlot].CopyFrom(365);   // a wooden torch

        // An empty torch is how the mode arranges darkness: whether the square
        // ends up lit is for the game to work out from what is being carried.
        game.Inventory[Inventory.LightSlot].P1 = (short)(test.Lit ? 5000 : 0);
        game.Inventory.EquipmentCount = 3;

        loop.Equipment.Recalculate();

        player.MaxHitPoints = 500;
        player.CurrentHitPoints = 500;

        loop.EnterLevel();

        // The books, in the order the object table holds them, so both sides
        // carry the same ones and the letters do not move.
        int carried = 0;

        for (int i = 0; i < GameTables.ObjectList.Length && carried < test.Books; i++)
        {
            if (GameTables.ObjectList[i].TVal == bookCategory)
            {
                var held = new InvenType();
                held.CopyFrom(i);
                game.Inventory.Carry(held);
                carried++;
            }
        }

        // Set after the stats, which work out what the character is entitled to
        // and would forget anything given to them before it.
        player.SpellLearned = test.Known;
        player.SpellWorked = 0;
        player.SpellForgotten = 0;
        Array.Fill(player.SpellOrder, Player.NoSpell);

        // Learning writes at the first free slot, so what is already known has
        // to be in the order as well as in the mask.
        uint holder = test.Known;
        int slot = 0;

        while (holder != 0)
        {
            int spell = System.Numerics.BitOperations.TrailingZeroCount(holder);
            player.SpellOrder[slot] = (byte)spell;
            slot++;
            holder &= holder - 1;
        }

        player.Blind = test.Blind;
        player.Confused = test.Confused;
        player.NewSpells = test.NewSpells;
        player.Status = 0;

        // Zero, so that the mana a first spell brings with it is worked out by
        // CalcMana() rather than stated here.
        player.MaxMana = 0;
        player.CurrentMana = 0;
        player.ManaFraction = 0;

        display.MessageWaitingFlag = false;
        loop.FreeTurn = false;

        // The list only exists between one key and the next, so both the cursor
        // and the whole screen are logged at every prompt.
        Action stopLogging = LogKeysAndScreens(output, screen);
        loop.Magic.GainSpells();
        stopLogging();

        output.Write(string.Join(
            ' ',
            "learned", player.SpellLearned.ToString(CultureInfo.InvariantCulture),
            "worked", player.SpellWorked.ToString(CultureInfo.InvariantCulture),
            "forgot", player.SpellForgotten.ToString(CultureInfo.InvariantCulture)) + "\n");

        output.Write(string.Join(
            ' ',
            "newspells", N(player.NewSpells),
            "status", player.Status.ToString(CultureInfo.InvariantCulture),
            "free", loop.FreeTurn ? "1" : "0") + "\n");

        output.Write(string.Join(
            ' ',
            "mana", N(player.MaxMana),
            "cmana", N(player.CurrentMana),
            "frac", N(player.ManaFraction),
            "stat", N(stat)) + "\n");

        output.Write(string.Join(
            ' ',
            "packed", N(game.Inventory.Count),
            "message", screen.GetRow(0).TrimEnd()) + "\n");

        output.Write("order");

        for (int i = 0; i < player.SpellOrder.Length; i++)
        {
            output.Write(" " + N(player.SpellOrder[i]));
        }

        output.Write("\n");

        // The screen as GainSpells() left it, which is what proves the restore
        // put back what the list was drawn over.
        DumpScreenRows(output, screen, "scr");

        Line(output, "final-state", game.Rng.State);
    }
}
