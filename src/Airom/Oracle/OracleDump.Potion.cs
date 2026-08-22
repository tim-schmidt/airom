// AIrom's side of the oracle's potion mode.
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
    /// Drinking things.
    ///
    /// Every potion in the table is drunk by a fresh character, and what it did
    /// is compared: the stats, the counters, the experience, the messages and
    /// whether the player worked out what it was.
    ///
    /// The C side runs the real quaff(), prompting and all, by putting the
    /// potion in the pack and feeding it the letter. This side calls the effects
    /// directly and then does what quaff() does around them - the food, and the
    /// item being used up.
    /// </summary>
    public static void DumpPotion(TextWriter output, uint seed, int first, int count)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "potion", seed);
        output.Write("first " + first.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("count " + count.ToString(CultureInfo.InvariantCulture) + "\n");


        // MagicInit shuffles the appearance tables where they stand, so calling
        // it once per potion would shuffle an already-shuffled table. It runs
        // once, and only the generator is re-seeded for each potion.
        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();

        for (int which = first;
             which < first + count && which < GameTables.ObjectList.Length;
             which++)
        {
            game.InitSeeds(seed);
            game.Turn = 0;
            game.DungeonLevel = 1;
            game.Player = new Player();
            game.Knowledge.Reset();

            var screen = new MemoryScreen { TypeAheadVisible = false };
            screen.SendKeys(new string(' ', 200));

            // No level is built here and nothing draws the map, so the panel
            // is left exactly as the C leaves it: untouched.
            var display = new Display(game, screen);

            var loop = new GameLoop(game, display);

            // Checked here rather than before the setup, because the original
            // re-seeds first and skips afterwards. A row that is neither a
            // potion nor food therefore leaves the generator freshly seeded,
            // and doing the skip earlier would leave it wherever the last real
            // item left it - a difference in the harness that would read as a
            // difference in the game.
            int category = GameTables.ObjectList[which].TVal;

            if (category != ItemCategory.Potion1 && category != ItemCategory.Potion2
                && category != ItemCategory.Food)
            {
                continue;
            }

            Player player = game.Player;

            // A character with room to improve in every direction: hurt,
            // drained, hungry and poisoned, so that a cure has something to
            // cure.
            player.Level = 10;
            player.ExperienceFactor = 100;
            player.Experience = 2000;
            player.MaxExperience = 5000;
            player.MaxHitPoints = 80;
            player.CurrentHitPoints = 30;
            player.MaxMana = 20;
            player.CurrentMana = 5;
            player.Food = 3000;
            player.Poisoned = 20;
            player.Confused = 20;
            player.Blind = 20;
            player.Afraid = 20;

            for (int i = 0; i < Stat.Count; i++)
            {
                player.MaxStat[i] = 16;
                player.CurrentStat[i] = 12;
                player.ModStat[i] = 0;
                loop.Stats.SetUseStat(i);
            }

            // The potion goes into the pack, as the C harness puts it there, so
            // that using it up is compared as well as drinking it.
            game.Inventory.Reset();

            var carried = new InvenType();
            carried.CopyFrom(which);
            game.Inventory.Carry(carried);

            // The real quaff() and eat(), less the prompt that picks which:
            // the effects, the experience for working out what it was, the
            // nourishment, and using it up.
            if (category == ItemCategory.Food)
            {
                loop.Food.Consume(0);
            }
            else
            {
                loop.Potions.Drink(0);
            }

            // Asked about a fresh copy rather than the slot, which the potion
            // has just been destroyed out of.
            var sample = new InvenType();
            sample.CopyFrom(which);

            string N(int value) => value.ToString(CultureInfo.InvariantCulture);

            output.Write(string.Join(
                ' ',
                "potion",
                N(which),
                "tval",
                N(category),
                "flags",
                GameTables.ObjectList[which].Flags.ToString(CultureInfo.InvariantCulture))
                + "\n");

            output.Write(string.Join(
                ' ',
                "  chp",
                N(player.CurrentHitPoints),
                "mana",
                N(player.CurrentMana),
                "exp",
                N(player.Experience),
                "lev",
                N(player.Level),
                "food",
                N(player.Food)) + "\n");

            output.Write("  stats " + string.Join(
                ' ',
                N(player.CurrentStat[0]), N(player.CurrentStat[1]), N(player.CurrentStat[2]),
                N(player.CurrentStat[3]), N(player.CurrentStat[4]), N(player.CurrentStat[5]))
                + "\n");

            output.Write(string.Join(
                ' ',
                "  blind",
                N(player.Blind),
                "conf",
                N(player.Confused),
                "afraid",
                N(player.Afraid),
                "pois",
                N(player.Poisoned),
                "para",
                N(player.Paralysis)) + "\n");

            output.Write(string.Join(
                ' ',
                "  fast",
                N(player.Hasted),
                "slow",
                N(player.Slowed),
                "hero",
                N(player.Hero),
                "shero",
                N(player.SuperHero),
                "invuln",
                N(player.Invulnerable)) + "\n");

            output.Write(string.Join(
                ' ',
                "  heat",
                N(player.ResistHeat),
                "cold",
                N(player.ResistCold),
                "detinv",
                N(player.DetectInvisible),
                "infra",
                N(player.TimedInfravision),
                "prot",
                N(player.ProtectionFromEvil)) + "\n");

            output.Write(string.Join(
                ' ',
                "  known",
                game.Knowledge.IsKindKnown(sample) ? "1" : "0",
                "packed",
                N(game.Inventory.Count),
                "message",
                screen.GetRow(0).TrimEnd()) + "\n");


            output.Write("  state "
                + game.Rng.State.ToString(CultureInfo.InvariantCulture) + "\n");

        }

        // The live state, not the last item's: the original prints whatever the
        // generator holds when the loop ends, and the loop can end on a row it
        // skipped.
        Line(output, "final-state", game.Rng.State);
    }
}
