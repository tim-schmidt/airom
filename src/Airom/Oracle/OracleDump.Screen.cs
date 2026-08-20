// AIrom's side of the oracle's screen mode.
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
    /// A drawn map.
    ///
    /// The C side links the real io.c against a curses that records into a grid,
    /// so what is compared is the composed screen rather than the cave behind
    /// it. That reaches the panel arithmetic - the conversion from dungeon
    /// coordinates to screen ones, which is the part most likely to be off by
    /// one and the hardest to spot in a grid dump.
    ///
    /// The level is lit and marked as seen throughout, so a glyph has to be
    /// decided for every square rather than blanks returned for the unexplored
    /// parts.
    /// </summary>
    public static void DumpScreen(TextWriter output, uint seed, int level) =>
        DumpScreenAt(output, seed, level, hallucinating: 0, mode: "screen");

    /// <summary>
    /// The same map, drawn by a hallucinating character.
    ///
    /// One square in twelve comes out as something else entirely, and both the
    /// roll and the character it picks come from the generator - so a map drawn
    /// while hallucinating has to consume exactly the same numbers on both
    /// sides, not merely look similar.
    /// </summary>
    public static void DumpHallucinate(TextWriter output, uint seed, int level) =>
        DumpScreenAt(output, seed, level, hallucinating: 5, mode: "hallucinate");

    private static void DumpScreenAt(
        TextWriter output, uint seed, int level, int hallucinating, string mode)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, mode, seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;
        game.Objects.Reset();
        game.Monsters.Reset();
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();

        new DungeonGenerator(game).GenerateCave();

        var screen = new MemoryScreen();
        var display = new Display(game, screen);

        Cave cave = game.Cave;

        // Light the level and mark it seen, so the map is drawn rather than
        // hidden, and make every monster visible.
        for (int row = 0; row < cave.Height; row++)
        {
            for (int column = 0; column < cave.Width; column++)
            {
                cave[row, column].PermanentLight = true;
                cave[row, column].FieldMark = true;
            }
        }

        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            game.Monsters[i].Visible = true;
        }

        // The player occupies index 1 of the monster list.
        cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

        display.Panel.Resize(cave.Height, cave.Width);
        display.Panel.Follow(game.CharacterRow, game.CharacterColumn, force: true);

        void Value(string key, int value) =>
            output.Write(key + " " + value.ToString(CultureInfo.InvariantCulture) + "\n");

        Value("panel-row", display.Panel.Row);
        Value("panel-col", display.Panel.Column);
        Value("panel-row-min", display.Panel.RowMin);
        Value("panel-col-min", display.Panel.ColumnMin);
        Value("panel-row-prt", display.Panel.RowOffset);
        Value("panel-col-prt", display.Panel.ColumnOffset);
        Value("char-row", game.CharacterRow);
        Value("char-col", game.CharacterColumn);

        // Hallucination is set after the level is built, so the map is the same
        // one the plain screen mode draws and only the drawing differs.
        game.Player.Hallucinating = hallucinating;

        display.ClearScreen();
        display.PrintMap();

        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write(
                "scr " + row.ToString(CultureInfo.InvariantCulture)
                + " " + screen.GetRow(row).TrimEnd() + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }
}
