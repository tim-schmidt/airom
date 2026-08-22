// AIrom's side of the oracle's spell and prayer modes.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Oracle;

/// <summary>
/// A caster that answers the question the prompting front of cast() asks.
///
/// Only the book is answered here. Everything after it - which spell, whether
/// to press on without the mana, which way it goes - runs through the real
/// prompts, reading the same scripted keys the C harness feeds itself.
/// </summary>
internal sealed class ScriptedMagic(GameState game, Display display, GameLoop loop)
    : Magic(game, display, loop)
{
    private readonly Display _display = display;

    protected internal override int? ChooseBook(string prompt, int first, int last)
    {
        // get_item() writes its prompt with prt(), which erases the message
        // line and flushes anything waiting on it. The prompt itself is then
        // overwritten by the one that asks which spell, so only the erasing
        // matters here.
        _display.Print(string.Empty, Display.MessageLine, 0);
        return 0;
    }
}

public static partial class OracleDump
{
    /// <summary>
    /// Casting a mage's spells and reciting a priest's prayers.
    ///
    /// Every spell a class has is cast twice by the same character on the same
    /// freshly generated level: once with mana to spare and once with almost
    /// none, since running short changes how likely the spell is to fail, what
    /// it asks before casting, and what it costs when it goes off anyway.
    /// </summary>
    public static void DumpMagic(
        TextWriter output, string mode, uint seed, int level, int first, int count)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(mode);

        bool mage = mode == "spell";
        int bookCategory = mage ? ItemCategory.MagicBook : ItemCategory.PrayerBook;

        Header(output, mode, seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("first " + first.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("count " + count.ToString(CultureInfo.InvariantCulture) + "\n");

        // MagicInit shuffles the appearance tables where they stand, so it runs
        // once and only the generator is re-seeded for each cast.
        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();


        for (int spell = first; spell < first + count && spell < Magic.SpellCount; spell++)
        {
            int book = -1;

            for (int which = 0; which < GameTables.ObjectList.Length; which++)
            {
                if (GameTables.ObjectList[which].TVal == bookCategory
                    && (GameTables.ObjectList[which].Flags & (1u << spell)) != 0)
                {
                    book = which;
                    break;
                }
            }

            if (book < 0)
            {
                continue;
            }

            // The lettering runs from the first spell printed in the book,
            // whether or not the character knows it.
            uint printed = GameTables.ObjectList[book].Flags;
            int firstSpell = System.Numerics.BitOperations.TrailingZeroCount(printed);
            char letter = (char)('a' + spell - firstSpell);

            for (int pass = 0; pass < 2; pass++)
            {
                // Plenty of mana, then almost none.
                int mana = pass == 0 ? 200 : 1;

                game.InitSeeds(seed);
                game.Turn = 0;
                game.DungeonLevel = level;
                game.Player = new Player { Class = mage ? 1 : 2 };
                game.Player.MaxDungeonLevel = level;
                game.Knowledge.Reset();

                var screen = new MemoryScreen { TypeAheadVisible = false };

                // The book is answered without a key, so the script starts at
                // the spell's own letter. Everything after it lines up with the
                // C harness's script one for one.
                bool confirm = GameTables.MagicSpell[game.Player.Class - 1][spell].Mana > mana;

                string script = letter.ToString(CultureInfo.InvariantCulture)
                    + (confirm ? "y" : string.Empty)
                    + (Aims(mage, spell) ? "6" : string.Empty);

                screen.SendKeys(script + new string((char)27, 600 - script.Length));

                var display = new Display(game, screen);

                // Generated with the screen in hand, so the panel is sized by
                // the arrival rather than by the harness afterwards.
                new DungeonGenerator(game, display).Generate();
                game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

                var loop = new GameLoop(game, display);
                loop.Spells = new ScriptedSpells(game, display, loop);
                loop.Magic = new ScriptedMagic(game, display, loop);

                Player player = game.Player;

                // Someone who can cast anything and survive what it wakes. No
                // experience to start with, since the level is pinned.
                player.Level = 40;
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

                // A weapon, a suit of armour and a light: without a light
                // nothing can be read at all, spell book included.
                game.Inventory.Reset();

                game.Inventory[Inventory.WieldSlot].CopyFrom(30);    // a stiletto
                game.Inventory[Inventory.BodySlot].CopyFrom(103);    // soft leather armor
                game.Inventory[Inventory.HeadSlot].CopyFrom(96);     // a hard leather cap
                game.Inventory[Inventory.LightSlot].CopyFrom(365);   // a wooden torch
                game.Inventory[Inventory.LightSlot].P1 = 5000;
                game.Inventory.EquipmentCount = 4;

                // Recalculating works out the hit points from the class and the
                // constitution, so the survivable totals are set after it.
                loop.Equipment.Recalculate();

                player.MaxHitPoints = 500;
                player.CurrentHitPoints = 500;
                player.MaxMana = 200;
                player.CurrentMana = mana;
                player.ManaFraction = 0;

                // The torch above is the whole of the arrangement: whether it
                // amounts to a light is arriving's conclusion to draw.
                loop.EnterLevel();

                var carried = new InvenType();
                carried.CopyFrom(book);
                game.Inventory.Carry(carried);

                // Set after the stats, which would otherwise forget the spells
                // the character is not yet entitled to.
                player.SpellLearned = 0x7FFFFFFFu;
                player.SpellWorked = 0;
                player.SpellForgotten = 0;
                Array.Fill(player.SpellOrder, Player.NoSpell);

                // Generating the level and lighting it can leave a message
                // waiting, and a waiting message turns the first prompt into a
                // -more- that eats a scripted key.
                display.MessageWaitingFlag = false;
                loop.FreeTurn = false;
                loop.NewLevel = false;

                if (mage)
                {
                    loop.Magic.Cast();
                }
                else
                {
                    loop.Magic.Pray();
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

                output.Write(string.Join(
                    ' ', "spell", N(spell), "pass", N(pass), "book", N(book),
                    "letter", letter.ToString(CultureInfo.InvariantCulture)) + "\n");

                output.Write(string.Join(
                    ' ',
                    "  chp", N(player.CurrentHitPoints),
                    "mana", N(player.CurrentMana),
                    "frac", N(player.ManaFraction),
                    "exp", N(player.Experience),
                    "free", loop.FreeTurn ? "1" : "0",
                    "newlev", loop.NewLevel ? "1" : "0") + "\n");

                output.Write(string.Join(
                    ' ',
                    "  at", N(game.CharacterRow), N(game.CharacterColumn),
                    "para", N(player.Paralysis),
                    "conf", N(player.Confused),
                    "afraid", N(player.Afraid),
                    "prot", N(player.ProtectionFromEvil),
                    "invuln", N(player.Invulnerable)) + "\n");

                output.Write(string.Join(
                    ' ',
                    "  fast", N(player.Hasted),
                    "blessed", N(player.Blessed),
                    "heat", N(player.ResistHeat),
                    "cold", N(player.ResistCold),
                    "detinv", N(player.DetectInvisible)) + "\n");

                output.Write(string.Join(
                    ' ',
                    "  worked", player.SpellWorked.ToString(CultureInfo.InvariantCulture),
                    "learned", player.SpellLearned.ToString(CultureInfo.InvariantCulture),
                    "forgot", player.SpellForgotten.ToString(CultureInfo.InvariantCulture),
                    "newspells", N(player.NewSpells)) + "\n");

                output.Write(string.Join(
                    ' ',
                    "  monsters", N(game.Monsters.Count - MonsterPool.FirstIndex),
                    "objects", N(game.Objects.Count - ObjectPool.FirstIndex),
                    "lit", N(lit),
                    "marked", N(marked),
                    "walls", N(walls)) + "\n");

                output.Write(string.Join(
                    ' ',
                    "  con", N(player.CurrentStat[Stat.Constitution]),
                    "food", N(player.Food),
                    "packed", N(game.Inventory.Count),
                    "message", screen.GetRow(0).TrimEnd()) + "\n");

                output.Write("  state "
                    + game.Rng.State.ToString(CultureInfo.InvariantCulture) + "\n");

            }
        }

        // The live state, not the last item's: the original prints whatever
        // the generator holds when the loop ends, and the loop can end on a
        // row it skipped - or run over rows that hold nothing at all.
        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>
    /// Whether a spell asks which way it goes, and so needs a direction key in
    /// the script. Read straight off the switches in magic.c and prayer.c.
    /// </summary>
    private static bool Aims(bool mage, int spell) => mage
        ? (spell + 1) is 1 or 7 or 8 or 9 or 11 or 15 or 16 or 20 or 23 or 24
            or 25 or 27 or 29
        : (spell + 1) is 9 or 18;
}
