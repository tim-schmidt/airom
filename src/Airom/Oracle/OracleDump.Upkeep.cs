// AIrom's side of the oracle's upkeep mode.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;
using Airom.Terminal;

namespace Airom.Oracle;

public static partial class OracleDump
{
    /// <summary>
    /// The turn: what happens to the player between one command and the next.
    ///
    /// The C side runs the real dungeon(), driven the only way a headless
    /// harness can drive it - the character is paralysed for the length of the
    /// run, so no command is asked for, and a quit is left in the key script for
    /// the turn the paralysis wears off. This side runs its own loop the same
    /// way, so the two are compared turn for turn rather than call for call.
    ///
    /// The monsters are cleared off the level first. Creature movement is not
    /// ported yet, and a monster taking its turn would consume random numbers on
    /// the C side only.
    /// </summary>
    public static void DumpUpkeep(TextWriter output, uint seed, int turns, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "upkeep", seed);
        output.Write("turns " + turns.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();

        // A human warrior, so the character is the same in every variation.
        Player player = new CharacterCreation(game)
            .Create(race: 0, characterClass: 0, male: true, name: "Oracle");
        game.Player = player;

        game.DungeonLevel = 1;
        game.Objects.Reset();
        game.Monsters.Reset();
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();
        new DungeonGenerator(game).CarveCave();

        // Empty the monster list, and take the monsters off the map with it.
        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                game.Cave[row, column].MonsterIndex = 0;
            }
        }

        game.Monsters.Reset();

        // What play_game() does after create_character(), less the starting
        // inventory: the pack is not ported yet, so there is nothing to carry
        // and no light to burn.
        player.Food = 7500;
        player.FoodDigested = 2;

        player.MaxMana = 20;
        player.CurrentMana = 0;
        player.ManaFraction = 0;
        player.CurrentHitPoints = 3;
        player.HitPointFraction = 0;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        // generate_cave() sizes the panel to the level it just carved; on this
        // side that is still the caller's job.
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        switch (variation)
        {
            case 0: break;
            case 1: player.Hero = 5; break;
            case 2: player.SuperHero = 5; break;
            case 3: player.Blind = 4; break;
            case 4: player.Confused = 4; break;
            case 5: player.Afraid = 4; break;
            case 6: player.Poisoned = 6; break;
            case 7: player.Hasted = 3; break;
            case 8: player.Slowed = 3; break;
            case 9: player.Invulnerable = 3; break;
            case 10: player.Blessed = 3; break;
            case 11:
                player.ProtectionFromEvil = 3;
                player.ResistHeat = 2;
                player.ResistCold = 2;
                break;
            case 12: player.DetectInvisible = 3; break;
            case 13: player.TimedInfravision = 3; break;
            case 14: player.Hallucinating = 3; break;
            case 15: player.Food = 1500; break;
            case 16: player.Food = 500; break;
            case 17: player.Food = 100; break;
            case 18: player.Food = -100; break;
            case 19: player.WordOfRecall = 3; break;
            case 20:
                player.Rest = 20;
                player.Status |= PlayerStatus.Resting;
                break;
            case 21: player.Regenerates = true; break;
            case 22:
                // Fear and heroism together: heroism cancels the fear rather
                // than counting it down.
                player.Afraid = 9;
                player.Hero = 4;
                break;
            case 23:
                player.Status |= PlayerStatus.Searching;
                break;
            case 24:
                // A lit lamp with plenty of oil: the player carries their own
                // light, so every step lights the squares around them.
                game.LightFuel = 400;
                game.PlayerLight = true;
                break;
            default:
                // A lamp about to run dry: it warns while it lasts, then goes
                // out.
                game.LightFuel = 12;
                game.PlayerLight = true;
                break;
        }

        // The C character comes out of creation with two recompute requests
        // raised by carrying the starting inventory. The pack is not ported, so
        // they are set here instead: the loop acts on them either way.
        player.Status |= PlayerStatus.WeightChanged | PlayerStatus.ArmourChanged;

        // Paralysed for the run, so no command is asked for until it wears off.
        player.Paralysis = turns;

        // Quit is ^K in the original key set, then a yes to confirm. The spaces
        // around it answer any -more- the run puts up - a starving character can
        // faint several times in a long run - and are harmless as commands.
        screen.SendKeys(
            new string(' ', 200) + Keys.Control('K') + "y" + new string(' ', 1798));

        // Cleared so that what is left on the screen was drawn by the loop
        // rather than by the character creation before it.
        display.ClearScreen();

        loop.Run();

        void Line(string text) => output.Write(text + "\n");
        string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        Line("turn " + N(game.Turn));
        // The whole status word, not just the sidebar bits: with the two
        // recompute requests mirrored above, both sides carry the same flags.
        Line("status " + player.Status.ToString(CultureInfo.InvariantCulture));
        Line("chp " + N(player.CurrentHitPoints) + " frac " + N(player.HitPointFraction)
            + " mhp " + N(player.MaxHitPoints));
        Line("cmana " + N(player.CurrentMana) + " frac " + N(player.ManaFraction));
        Line("food " + N(player.Food) + " digested " + N(player.FoodDigested));
        Line("speed " + N(player.Speed));
        Line("bth " + N(player.BaseToHit) + " bthb " + N(player.BaseToHitBows));
        Line("pac " + N(player.ArmourClass) + " dis_ac " + N(player.DisplayedArmourClass));
        Line("hero " + N(player.Hero) + " shero " + N(player.SuperHero)
            + " blessed " + N(player.Blessed) + " invuln " + N(player.Invulnerable));
        Line("blind " + N(player.Blind) + " confused " + N(player.Confused)
            + " afraid " + N(player.Afraid) + " poisoned " + N(player.Poisoned));
        Line("fast " + N(player.Hasted) + " slow " + N(player.Slowed)
            + " image " + N(player.Hallucinating) + " paralysis " + N(player.Paralysis));
        Line("protevil " + N(player.ProtectionFromEvil) + " heat " + N(player.ResistHeat)
            + " cold " + N(player.ResistCold));
        Line("detect_inv " + N(player.DetectInvisible)
            + " see_inv " + (player.SeeInvisible ? "1" : "0")
            + " tim_infra " + N(player.TimedInfravision)
            + " see_infra " + N(player.SeeInfrared));
        Line("word_recall " + N(player.WordOfRecall) + " dun_level " + N(game.DungeonLevel)
            + " max_dlv " + N(player.MaxDungeonLevel));
        Line("rest " + N(player.Rest));
        Line("death " + (loop.Dead ? "1" : "0") + " died_from " + loop.DiedFrom);
        Line("monsters " + N(game.Monsters.Count - MonsterPool.FirstIndex));

        for (int row = 0; row < screen.Rows; row++)
        {
            Line("up " + N(row) + " " + screen.GetRow(row).TrimEnd());
        }

        OracleDump.Line(output, "final-state", game.Rng.State);
    }
}
