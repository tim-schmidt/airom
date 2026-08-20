// AIrom's side of the oracle's town mode.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;

namespace Airom.Oracle;

public static partial class OracleDump
{
    /// <summary>
    /// The town, complete.
    ///
    /// Everything is compared: the six shops and their doors, the stairs, the
    /// lighting, the townsfolk, and the stock each shop restocks with.
    /// </summary>
    /// <param name="turn">
    /// Turns elapsed, which decides day or night. The clock alternates in blocks
    /// of 5000, so 0 is daytime and 5000 is night.
    /// </param>
    public static void DumpTown(TextWriter output, uint seed, int turn)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "town", seed);
        output.Write("turn " + turn.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = 0;
        game.Turn = turn;

        game.Objects.Reset();
        game.Monsters.Reset();
        game.Cave.Resize(DungeonGenerator.TownHeight, DungeonGenerator.TownWidth);
        game.Cave.Blank();
        game.Stores.Initialise();

        new DungeonGenerator(game).GenerateTown();

        Cave cave = game.Cave;
        output.Write("char-row " + game.CharacterRow.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("char-col " + game.CharacterColumn.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("phase " + ((1 & (turn / 5000)) != 0 ? "night" : "day") + "\n");

        for (int i = 0; i < game.Stores.All.Length; i++)
        {
            Store shop = game.Stores.All[i];
            output.Write(string.Join(
                ' ',
                "shop",
                i.ToString(CultureInfo.InvariantCulture),
                shop.Owner.ToString(CultureInfo.InvariantCulture),
                shop.StockCount.ToString(CultureInfo.InvariantCulture)) + "\n");
        }
        output.Write("height " + cave.Height.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("width " + cave.Width.ToString(CultureInfo.InvariantCulture) + "\n");

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

        for (int y = 0; y < cave.Height; y++)
        {
            for (int x = 0; x < cave.Width; x++)
            {
                line[x] = cave[y, x].PermanentLight ? 'L' : '.';
            }

            output.Write(
                "lit " + y.ToString(CultureInfo.InvariantCulture) + " " + new string(line) + "\n");
        }

        int monsterCount = game.Monsters.Count - MonsterPool.FirstIndex;
        output.Write("monsters " + monsterCount.ToString(CultureInfo.InvariantCulture) + "\n");
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
                monster.Sleep.ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        int objectCount = game.Objects.Count - ObjectPool.FirstIndex;
        output.Write("objects " + objectCount.ToString(CultureInfo.InvariantCulture) + "\n");
        for (int i = ObjectPool.FirstIndex; i < game.Objects.Count; i++)
        {
            output.Write(string.Join(
                ' ',
                "object",
                i.ToString(CultureInfo.InvariantCulture),
                game.Objects[i].Index.ToString(CultureInfo.InvariantCulture),
                game.Objects[i].TVal.ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        for (int y = 0; y < cave.Height; y++)
        {
            for (int x = 0; x < cave.Width; x++)
            {
                if (cave[y, x].ObjectIndex != 0)
                {
                    output.Write(string.Join(
                        ' ',
                        "at",
                        y.ToString(CultureInfo.InvariantCulture),
                        x.ToString(CultureInfo.InvariantCulture),
                        cave[y, x].ObjectIndex.ToString(CultureInfo.InvariantCulture)) + "\n");
                }
            }
        }

        Line(output, "final-state", game.Rng.State);
    }
}
