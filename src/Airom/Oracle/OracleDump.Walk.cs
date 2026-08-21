// AIrom's side of the oracle's walk and run modes.
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
    /// The directions a scripted walk tries, in order. Numbered as the number
    /// pad is, so the table reads the same on both sides.
    /// </summary>
    private static readonly int[] WalkScript = [6, 2, 4, 8, 3, 1, 9, 7];

    /// <summary>
    /// Sets up a level for a walk: the monsters and the loose objects are taken
    /// off it first.
    ///
    /// Monsters would move on the C side only, since creature.c is not ported.
    /// Objects would be picked up on the C side only, since carry() needs the
    /// inventory - and picking one up prints a message and changes the pack,
    /// which is exactly the kind of divergence that would drown out what is
    /// being compared. Doors and stairs go with them, being objects here too.
    /// </summary>
    private static (GameState Game, Display Display, MemoryScreen Screen, GameLoop Loop)
        StrippedLevel(uint seed, int level)
    {
        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Turn = 0;
        game.DungeonLevel = level;
        game.Player.Level = 1;
        game.Player.MaxDungeonLevel = level;

        new DungeonGenerator(game).Generate();

        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                game.Cave[row, column].MonsterIndex = 0;
                game.Cave[row, column].ObjectIndex = 0;
            }
        }

        game.Monsters.Reset();
        game.Objects.Reset();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        var display = new Display(game, screen);
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);
        display.Panel.Invalidate();

        return (game, display, screen, new GameLoop(game, display));
    }

    private static void DumpKnownSquares(TextWriter output, GameState game)
    {
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
    }

    /// <summary>
    /// Walking, one step at a time.
    ///
    /// move_char() is the whole of a step: it moves the record, drags the light
    /// after it, lights a room on entry, searches what is nearby, and decides
    /// whether a wall blocks the way. The script walks a fixed cycle of
    /// directions rather than choosing cleverly, so both sides walk into the
    /// same walls.
    /// </summary>
    public static void DumpWalk(TextWriter output, uint seed, int level, int steps, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "walk", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("steps " + steps.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        (GameState game, Display display, MemoryScreen screen, GameLoop loop) =
            StrippedLevel(seed, level);

        // A searcher good enough to roll for something every step, so the search
        // inside move_char() is exercised rather than skipped.
        game.Player.SearchFrequency = 1;
        game.Player.Search = 40;

        switch (variation)
        {
            case 0:
                game.PlayerLight = true;
                break;
            case 1:
                // Confused: three steps in four go somewhere else entirely,
                // which draws random numbers of its own.
                game.PlayerLight = true;
                game.Player.Confused = 30000;
                break;
            case 2:
                game.PlayerLight = true;
                game.Player.Blind = 30000;
                break;
            default:
                game.PlayerLight = false;
                break;
        }

        loop.Lighting.CheckView();

        for (int step = 0; step < steps; step++)
        {
            loop.FreeTurn = false;
            loop.Movement.MoveChar(WalkScript[step % 8], pickUp: true);

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

        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write(
                "walk " + row.ToString(CultureInfo.InvariantCulture)
                + " " + screen.GetRow(row).TrimEnd() + "\n");
        }

        DumpKnownSquares(output, game);
        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>
    /// Running: a step repeated until something worth stopping for turns up.
    ///
    /// find_init() decides what kind of run this is from the two squares either
    /// side of the first step, and every step after that asks area_affect() where
    /// to go next. The path is dumped square by square, so a run that turns one
    /// corner differently shows up immediately rather than only in the final
    /// position.
    /// </summary>
    public static void DumpRun(TextWriter output, uint seed, int level, int direction, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "run", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("direction " + direction.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        (GameState game, Display display, MemoryScreen screen, GameLoop loop) =
            StrippedLevel(seed, level);

        // No searching while running: it would draw a random number a step and
        // drown the run itself in noise.
        game.Player.SearchFrequency = 30000;
        game.Player.Search = 0;
        game.PlayerLight = true;

        switch (variation)
        {
            case 0:
                break;
            case 1:
                // Never cut a corner: go the long way round instead.
                game.CutCorners = false;
                break;
            case 2:
                // Stop at anything that might be a corner rather than examining
                // it.
                game.ExamineCorners = false;
                break;
            default:
                // Draw the player while running, which keeps the lamp lit.
                game.ShowSelfWhileRunning = true;
                break;
        }

        loop.Lighting.CheckView();

        output.Write(string.Join(
            ' ',
            "start",
            game.CharacterRow.ToString(CultureInfo.InvariantCulture),
            game.CharacterColumn.ToString(CultureInfo.InvariantCulture)) + "\n");

        loop.Movement.FindInit(direction);

        int guard = 0;
        while (guard < 300 && loop.Running)
        {
            output.Write(string.Join(
                ' ',
                "at",
                game.CharacterRow.ToString(CultureInfo.InvariantCulture),
                game.CharacterColumn.ToString(CultureInfo.InvariantCulture)) + "\n");

            loop.Movement.FindRun();
            guard++;
        }

        output.Write(string.Join(
            ' ',
            "stopped",
            game.CharacterRow.ToString(CultureInfo.InvariantCulture),
            game.CharacterColumn.ToString(CultureInfo.InvariantCulture),
            "after",
            guard.ToString(CultureInfo.InvariantCulture)) + "\n");

        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write(
                "run " + row.ToString(CultureInfo.InvariantCulture)
                + " " + screen.GetRow(row).TrimEnd() + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }
}
