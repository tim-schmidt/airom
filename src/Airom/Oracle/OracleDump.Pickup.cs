// AIrom's side of the oracle's pickup mode.
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
    /// What the player is rung with: gold, a weapon, armour, a potion, a scroll,
    /// food, a wand, and a pile of pebbles that has to stack.
    /// </summary>
    private static readonly int[] PickupRing = [399, 74, 91, 222, 173, 163, 293, 82];

    /// <summary>
    /// Walking over things and picking them up.
    ///
    /// The walk mode strips the level bare; this one leaves the objects where
    /// they fell, so every step onto one runs carry(): the gold into the purse,
    /// the rest into the pack, with the weight and the sorting that follow.
    ///
    /// The traps are taken off first. hit_trap() belongs to moria3.c and is not
    /// ported, so a trap would fire on the C side only.
    /// </summary>
    public static void DumpPickup(TextWriter output, uint seed, int level, int steps, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "pickup", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("steps " + steps.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Turn = 0;
        game.DungeonLevel = level;
        game.Player.Level = 1;
        game.Player.MaxDungeonLevel = level;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        var display = new Display(game, screen);

        // Generated with the screen in hand, so the panel is sized by the
        // arrival rather than by the harness afterwards.
        new DungeonGenerator(game, display).Generate();

        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                CaveSquare square = game.Cave[row, column];
                square.MonsterIndex = 0;

                if (square.ObjectIndex != 0)
                {
                    int category = game.Objects[square.ObjectIndex].TVal;
                    if (category is ItemCategory.InvisibleTrap or ItemCategory.VisibleTrap
                        or ItemCategory.StoreDoor)
                    {
                        square.ObjectIndex = 0;
                    }
                }
            }
        }

        game.Monsters.Reset();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

        // A scripted walk wanders in a small circle, so what it finds is left to
        // chance. Ring the player with things instead.
        int k = 0;
        for (int row = game.CharacterRow - 1; row <= game.CharacterRow + 1; row++)
        {
            for (int column = game.CharacterColumn - 1; column <= game.CharacterColumn + 1; column++)
            {
                if (row == game.CharacterRow && column == game.CharacterColumn)
                {
                    continue;
                }

                if (game.Cave[row, column].Feature > CaveFeature.MaxOpenSpace)
                {
                    k++;
                    continue;
                }

                int slot = game.Objects.Allocate();
                game.Objects[slot].CopyFrom(PickupRing[k]);

                if (PickupRing[k] == 399)
                {
                    game.Objects[slot].Cost = 250;
                }

                if (PickupRing[k] == 82)
                {
                    game.Objects[slot].Number = 12;
                }

                game.Cave[row, column].ObjectIndex = slot;
                k++;
            }
        }

        Player player = game.Player;

        // Strong enough to carry a good deal, so the weight limit is reached by
        // picking things up rather than by starting encumbered.
        player.CurrentStat[Stat.Strength] = 16;
        player.MaxStat[Stat.Strength] = 16;
        player.UseStat[Stat.Strength] = 16;
        player.Weight = 150;
        player.SearchFrequency = 1;
        player.Search = 40;
        LightTheLamp(game, 400);

        switch (variation)
        {
            case 0:
                break;
            case 1:
                // Ask before picking anything up, and answer yes to everything.
                game.PromptBeforeCarrying = true;
                break;
            case 2:
                // A weakling, who reaches the weight limit almost at once.
                player.CurrentStat[Stat.Strength] = 3;
                player.MaxStat[Stat.Strength] = 3;
                player.UseStat[Stat.Strength] = 3;
                player.Weight = 80;
                break;
            default:
                // Walk over everything without picking any of it up.
                break;
        }

        // Spaces answer the -more- prompts and the pickup questions alike; a "y"
        // would be needed for a no, and yes is what these variations want.
        var script = new char[2000];
        for (int i = 0; i < script.Length; i++)
        {
            script[i] = i % 2 == 1 ? 'y' : ' ';
        }

        screen.SendKeys(new string(script));

        var loop = new GameLoop(game, display);
        loop.EnterLevel();

        for (int step = 0; step < steps; step++)
        {
            loop.FreeTurn = false;
            loop.Movement.MoveChar(WalkScript[step % 8], pickUp: variation != 3);

            output.Write(string.Join(
                ' ',
                "step",
                step.ToString(CultureInfo.InvariantCulture),
                "at",
                game.CharacterRow.ToString(CultureInfo.InvariantCulture),
                game.CharacterColumn.ToString(CultureInfo.InvariantCulture),
                "free",
                loop.FreeTurn ? "1" : "0") + "\n");
        }

        Inventory pack = game.Inventory;

        output.Write(string.Join(
            ' ',
            "gold",
            player.Gold.ToString(CultureInfo.InvariantCulture),
            "weight",
            pack.Weight.ToString(CultureInfo.InvariantCulture),
            "count",
            pack.Count.ToString(CultureInfo.InvariantCulture),
            "burden",
            pack.PackBurden.ToString(CultureInfo.InvariantCulture)) + "\n");

        for (int i = 0; i < pack.Count; i++)
        {
            output.Write(string.Join(
                ' ',
                "pack",
                i.ToString(CultureInfo.InvariantCulture),
                ((int)pack[i].Number).ToString(CultureInfo.InvariantCulture),
                ((int)pack[i].Weight).ToString(CultureInfo.InvariantCulture),
                game.Names.Describe(pack[i], withArticle: true)) + "\n");
        }

        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write(
                "pick " + row.ToString(CultureInfo.InvariantCulture)
                + " " + screen.GetRow(row).TrimEnd() + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }
}
