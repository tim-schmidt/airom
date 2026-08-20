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
    /// Only creatures with no spells are used. mon_cast_spell() reaches into
    /// spells.c, which is not ported, so a spellcaster would draw random numbers
    /// on one side that the other never draws.
    /// </summary>
    public static void DumpMonsters(TextWriter output, uint seed, int level, int turns, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "monsters", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("turns " + turns.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Turn = 0;
        game.DungeonLevel = level;
        game.Player.Level = 1;
        game.Player.MaxDungeonLevel = level;

        new DungeonGenerator(game).Generate();

        // Replace every spellcaster with something from the same table entry
        // range that does not cast, so the comparison stays honest.
        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            Monster monster = game.Monsters[i];
            int guard = 0;

            while (GameTables.CreatureList[monster.CreatureIndex].CastsSpells
                   && guard < GameTables.CreatureList.Length)
            {
                monster.CreatureIndex =
                    (monster.CreatureIndex + 1) % GameTables.CreatureList.Length;
                guard++;
            }

            CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];
            monster.HitPoints = creature.HitDiceCount * creature.HitDiceSides;
            monster.Speed = creature.Speed - 10;
        }

        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;
        game.PlayerLight = true;

        Player player = game.Player;
        player.CurrentHitPoints = 2000;
        player.MaxHitPoints = 2000;
        player.Food = 7500;
        player.Stealth = 3;

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
            default:
                // Resting, which changes how often a sleeper checks and how many
                // moves a fast monster gets.
                player.Rest = 30000;
                player.Status |= PlayerStatus.Resting;
                break;
        }

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(new string(' ', 3999));

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

            if (memory.Move == 0 && memory.Wake == 0 && memory.Ignore == 0
                && memory.Attacks[0] == 0 && memory.Kills == 0)
            {
                continue;
            }

            output.Write(string.Join(
                ' ',
                "recall",
                N(i),
                "move",
                memory.Move.ToString(CultureInfo.InvariantCulture),
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
}
