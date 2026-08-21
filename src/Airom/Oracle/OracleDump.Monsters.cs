// AIrom's side of the oracle's monsters mode.
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
    /// The monsters taking their turns.
    ///
    /// A level is generated and left as it was found - monsters, objects and all
    /// - and then creatures() is called over and over with the player standing
    /// still. Everything the monsters do is compared: where they move, what they
    /// open, what they eat, what they breed, what they steal, and what the
    /// player learns about them along the way.
    ///
    /// The level is left exactly as generated, spellcasters included: they
    /// breathe, summon, blind, drain and teleport, and all of it is compared.
    /// </summary>
    public static void DumpMonsters(TextWriter output, uint seed, int level, int turns, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "monsters", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("turns " + turns.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Turn = 0;
        game.DungeonLevel = level;
        game.Player.Level = 1;
        game.Player.MaxDungeonLevel = level;

        new DungeonGenerator(game).Generate();

        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;
        game.PlayerLight = true;

        Player player = game.Player;
        player.CurrentHitPoints = 2000;
        player.MaxHitPoints = 2000;
        player.Food = 7500;
        player.Stealth = 3;

        int casters = -1;

        switch (variation)
        {
            case 0:
                break;
            case 1:
                // Asleep to begin with, so the waking rolls are exercised.
                for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
                {
                    game.Monsters[i].Sleep = 200;
                }

                break;
            case 2:
                // Aggravated: everything wakes at once and hurries.
                player.AggravatesMonsters = true;
                break;
            case 3:
                // Resting, which changes how often a sleeper checks and how many
                // moves a fast monster gets.
                player.Rest = 30000;
                player.Status |= PlayerStatus.Resting;
                break;
            default:
                // Ring the player with things that cast, awake and in range, so
                // the spells themselves are compared rather than waited for:
                // breaths, summonings, blindness, drained mana and the rest.
                casters = RingWithCasters(game);
                break;
        }

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(new string(' ', 3999));

        if (casters >= 0)
        {
            output.Write("casters " + casters.ToString(CultureInfo.InvariantCulture) + "\n");
        }

        var display = new Display(game, screen);
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);
        display.Panel.Invalidate();

        var loop = new GameLoop(game, display);
        loop.Lighting.CheckView();

        for (int i = 0; i < turns; i++)
        {
            game.Turn++;
            loop.MonsterAi.Creatures(true);
        }

        string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        output.Write(string.Join(
            ' ',
            "monsters",
            N(game.Monsters.Count - MonsterPool.FirstIndex),
            "bred",
            N(game.Monsters.BredCount)) + "\n");

        output.Write(string.Join(
            ' ',
            "chp",
            N(player.CurrentHitPoints),
            "gold",
            N(player.Gold),
            "packed",
            N(game.Inventory.Count)) + "\n");

        output.Write(string.Join(
            ' ',
            "blind",
            N(player.Blind),
            "confused",
            N(player.Confused),
            "afraid",
            N(player.Afraid),
            "poisoned",
            N(player.Poisoned),
            "paralysis",
            N(player.Paralysis)) + "\n");

        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            Monster monster = game.Monsters[i];

            output.Write(string.Join(
                ' ',
                "mon",
                N(i),
                "at",
                N(monster.Row),
                N(monster.Column),
                "kind",
                N(monster.CreatureIndex),
                "hp",
                N(monster.HitPoints),
                "sleep",
                N(monster.Sleep),
                "stun",
                N(monster.Stunned),
                "conf",
                N(monster.Confused),
                "ml",
                monster.Visible ? "1" : "0") + "\n");
        }

        // What the turns taught the player about each kind that took part.
        for (int i = 0; i < game.Memories.Count; i++)
        {
            MonsterMemory memory = game.Memories[i];

            if (memory.Move == 0 && memory.Spells == 0 && memory.Wake == 0
                && memory.Ignore == 0 && memory.Attacks[0] == 0 && memory.Kills == 0)
            {
                continue;
            }

            output.Write(string.Join(
                ' ',
                "recall",
                N(i),
                "move",
                memory.Move.ToString(CultureInfo.InvariantCulture),
                "spells",
                memory.Spells.ToString(CultureInfo.InvariantCulture),
                "wake",
                N(memory.Wake),
                "ignore",
                N(memory.Ignore),
                "attacks",
                N(memory.Attacks[0]),
                N(memory.Attacks[1]),
                N(memory.Attacks[2]),
                N(memory.Attacks[3])) + "\n");
        }

        output.Write("objects " + N(game.Objects.Count - ObjectPool.FirstIndex) + "\n");

        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write("mon " + N(row) + " " + screen.GetRow(row).TrimEnd() + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }
    /// <summary>
    /// Puts something that casts on every open square around the player, awake
    /// and looking at them.
    /// </summary>
    private static int RingWithCasters(GameState game)
    {
        var generator = new DungeonGenerator(game);
        int placed = 0;

        for (int kind = 0; kind < GameTables.CreatureList.Length && placed < 8; kind++)
        {
            CreatureType creature = GameTables.CreatureList[kind];

            if (!creature.CastsSpells || creature.Level > game.DungeonLevel + 10)
            {
                continue;
            }

            for (int direction = 1; direction <= 9; direction++)
            {
                if (direction == 5)
                {
                    continue;
                }

                int row = game.CharacterRow;
                int column = game.CharacterColumn;

                if (!game.Cave.Move(direction, ref row, ref column))
                {
                    continue;
                }

                CaveSquare square = game.Cave[row, column];

                if (square.Feature > CaveFeature.MaxOpenSpace || square.MonsterIndex != 0)
                {
                    continue;
                }

                if (generator.PlaceMonster(row, column, kind, asleep: false))
                {
                    game.Monsters[square.MonsterIndex].Sleep = 0;
                    game.Monsters[square.MonsterIndex].Visible = true;
                    placed++;
                }

                break;
            }
        }

        return placed;
    }
}
