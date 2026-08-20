// The full-level comparison: AIrom's side of the oracle's cave mode.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;

namespace Airom.Oracle;

public static partial class OracleDump
{
    /// <summary>
    /// A complete dungeon level, generated exactly as the game would.
    ///
    /// Unlike the earlier modes this fixes nothing: how many rooms there are,
    /// where they sit, which of the four builders makes each one, and the order
    /// the corridors join them in are all drawn from the generator. It is the
    /// whole of cave_gen, and matching it means the port produces the same
    /// dungeons the original did.
    ///
    /// Levels at or below 49 only: place_win_monster is not ported, and it fires
    /// from depth 50.
    /// </summary>
    public static void DumpCave(TextWriter output, uint seed, int level)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "cave", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;

        game.Objects.Reset();
        game.Monsters.Reset();
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();

        new DungeonGenerator(game).CarveCave();

        Cave cave = game.Cave;
        output.Write("height " + cave.Height.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("width " + cave.Width.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("char-row " + game.CharacterRow.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("char-col " + game.CharacterColumn.ToString(CultureInfo.InvariantCulture) + "\n");

        var line = new char[cave.Width];

        for (int y = 0; y < cave.Height; y++)
        {
            for (int x = 0; x < cave.Width; x++)
            {
                line[x] = FeatureChar(cave[y, x].Feature);
            }

            output.Write(
                "row " + y.ToString(CultureInfo.InvariantCulture) + " " + new string(line) + "\n");
        }

        // Lighting and field marks are separate from the terrain, and a
        // generator bug can get the layout right while getting these wrong.
        for (int y = 0; y < cave.Height; y++)
        {
            for (int x = 0; x < cave.Width; x++)
            {
                CaveSquare square = cave[y, x];
                int bits = (square.LitRoom ? 1 : 0)
                    + (square.FieldMark ? 2 : 0)
                    + (square.PermanentLight ? 4 : 0);
                line[x] = (char)('0' + bits);
            }

            output.Write(
                "flags " + y.ToString(CultureInfo.InvariantCulture) + " " + new string(line) + "\n");
        }

        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            Monster monster = game.Monsters[i];
            output.Write(string.Join(
                ' ',
                "monster",
                i.ToString(CultureInfo.InvariantCulture),
                monster.Row.ToString(CultureInfo.InvariantCulture),
                monster.Column.ToString(CultureInfo.InvariantCulture),
                monster.CreatureIndex.ToString(CultureInfo.InvariantCulture),
                monster.HitPoints.ToString(CultureInfo.InvariantCulture),
                monster.Speed.ToString(CultureInfo.InvariantCulture),
                monster.Sleep.ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        for (int i = ObjectPool.FirstIndex; i < game.Objects.Count; i++)
        {
            InvenType item = game.Objects[i];
            output.Write(string.Join(
                ' ',
                "object",
                i.ToString(CultureInfo.InvariantCulture),
                item.Index.ToString(CultureInfo.InvariantCulture),
                item.TVal.ToString(CultureInfo.InvariantCulture),
                item.SubVal.ToString(CultureInfo.InvariantCulture),
                item.Number.ToString(CultureInfo.InvariantCulture),
                item.Cost.ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }
}
