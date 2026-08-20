// AIrom's side of the oracle's light mode.
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
    /// The eight directions, in the order the walk tries them. Numbered as the
    /// number pad is, so the table reads the same on both sides.
    /// </summary>
    private static readonly int[] LightStepRows = [0, 1, 0, -1, 1, 1, -1, -1];

    private static readonly int[] LightStepColumns = [1, 0, -1, 0, 1, -1, 1, -1];

    /// <summary>
    /// The lighting: what the player can see, one step at a time.
    ///
    /// The player is walked along a fixed path - each step takes the first
    /// direction that is not a wall - and the screen is dumped at the end. That
    /// exercises the parts a stationary character never reaches: the block
    /// behind the player going dark, walls becoming permanently known, objects
    /// being noticed as the light passes over them, and a room lighting as its
    /// doorway is stepped into.
    ///
    /// move_char() belongs to moria2.c and is not ported, so the walk is done
    /// here: the square is picked, the player record moved, and the lighting told
    /// about it, which is the sequence move_char() itself uses.
    /// </summary>
    public static void DumpLight(TextWriter output, uint seed, int level, int steps, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "light", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("steps " + steps.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Turn = 0;
        game.DungeonLevel = level;
        game.Player.Level = 1;
        game.Player.MaxDungeonLevel = level;
        // The whole arrival, town or dungeon, as generate_cave() does it.
        new DungeonGenerator(game).Generate();

        // The monsters are cleared off: creature movement is not ported, and one
        // taking its turn would consume random numbers on one side only.
        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                game.Cave[row, column].MonsterIndex = 0;
            }
        }

        game.Monsters.Reset();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        var display = new Display(game, screen);
        var lighting = new Lighting(game, display);

        switch (variation)
        {
            case 0:
                // A lit lamp: the ordinary case, where the player carries their
                // light.
                game.PlayerLight = true;
                break;
            case 1:
                // Blind: nothing new is revealed, so only the player symbol
                // moves.
                game.PlayerLight = true;
                game.Player.Blind = 500;
                break;
            case 2:
                // No light at all, which is the same path as blindness.
                game.PlayerLight = false;
                break;
            default:
                // Running: the lamp is switched off, so a long run does not
                // repaint the same nine squares at every step.
                game.PlayerLight = true;
                game.Running = true;
                break;
        }

        // Sizing the panel to the level is still the caller's job on this side.
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);
        display.Panel.Invalidate();
        lighting.CheckView();

        for (int step = 0; step < steps; step++)
        {
            int row = game.CharacterRow;
            int column = game.CharacterColumn;
            int toRow = row;
            int toColumn = column;

            for (int i = 0; i < 8; i++)
            {
                int tryRow = row + LightStepRows[(step + i) % 8];
                int tryColumn = column + LightStepColumns[(step + i) % 8];

                if (game.Cave[tryRow, tryColumn].Feature <= CaveFeature.MaxOpenSpace)
                {
                    toRow = tryRow;
                    toColumn = tryColumn;
                    break;
                }
            }

            lighting.MoveRecord(row, column, toRow, toColumn);
            game.CharacterRow = toRow;
            game.CharacterColumn = toColumn;

            if (display.Panel.Follow(toRow, toColumn, force: false))
            {
                display.PrintMap();
            }

            lighting.MoveLight(row, column, toRow, toColumn);

            output.Write(string.Join(
                ' ',
                "step",
                step.ToString(CultureInfo.InvariantCulture),
                "at",
                toRow.ToString(CultureInfo.InvariantCulture),
                toColumn.ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        lighting.CheckView();

        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write(
                "lit " + row.ToString(CultureInfo.InvariantCulture)
                + " " + screen.GetRow(row).TrimEnd() + "\n");
        }

        // What the player now knows about the level, square by square, so a
        // difference in the flags shows even where the screen agrees.
        int permanent = 0;
        int temporary = 0;
        int marked = 0;

        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                CaveSquare square = game.Cave[row, column];
                if (square.PermanentLight)
                {
                    permanent++;
                }

                if (square.TemporaryLight)
                {
                    temporary++;
                }

                if (square.FieldMark)
                {
                    marked++;
                }
            }
        }

        output.Write(string.Join(
            ' ',
            "permanent",
            permanent.ToString(CultureInfo.InvariantCulture),
            "temporary",
            temporary.ToString(CultureInfo.InvariantCulture),
            "marked",
            marked.ToString(CultureInfo.InvariantCulture)) + "\n");

        output.Write(
            "light-flag " + (game.TemporaryLightOn ? "1" : "0")
            + " player-light " + (game.PlayerLight ? "1" : "0") + "\n");

        Line(output, "final-state", game.Rng.State);
    }
}
