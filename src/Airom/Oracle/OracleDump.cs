// AIrom's half of the reference comparison.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;
using Airom.Data;

namespace Airom.Oracle;

/// <summary>
/// Prints AIrom's state in the same plain-text format as the C oracle in
/// tools/oracle, so the two can be diffed line for line.
///
/// Umoria has no test suite, so "correct" here means "does what the C did".
/// Every dungeon, monster roll and item drop derives from one deterministic
/// generator, which makes an identical dump strong evidence that the whole
/// chain producing it agrees - far broader coverage than any unit test of the
/// same size.
///
/// The format is deliberately dull: line-oriented, ASCII, one fact per line,
/// so a diff points at the first divergence instead of a wall of noise.
/// </summary>
public static partial class OracleDump
{
    /// <summary>
    /// Format version. Must match ORACLE_FORMAT in tools/oracle/oracle_main.c;
    /// bump both together when the layout changes.
    /// </summary>
    public const int FormatVersion = 1;

    internal static void Header(TextWriter output, string mode, uint seed)
    {
        output.Write($"# airom-oracle {FormatVersion}\n");
        output.Write($"mode {mode}\n");
        output.Write(
            "seed " + seed.ToString(CultureInfo.InvariantCulture) + "\n");
    }

    /// <summary>
    /// The generator alone: seed it, then print a run of draws. This is the
    /// narrowest comparison available and the one that has to pass before any
    /// other dump means anything.
    /// </summary>
    public static void DumpRng(TextWriter output, uint seed, long count)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "rng", seed);
        output.Write("count " + count.ToString(CultureInfo.InvariantCulture) + "\n");

        var rng = new Rng(seed);
        output.Write(
            "state-after-set-seed "
            + rng.State.ToString(CultureInfo.InvariantCulture) + "\n");

        for (long i = 1; i <= count; i++)
        {
            int value = rng.Next();
            output.Write(
                "value " + i.ToString(CultureInfo.InvariantCulture)
                + " " + value.ToString(CultureInfo.InvariantCulture) + "\n");
        }

        output.Write(
            "final-state " + rng.State.ToString(CultureInfo.InvariantCulture) + "\n");
    }

    /// <summary>
    /// The seeding chain the game actually uses: init_seeds() derives the
    /// appearance and town seeds and burns a random number of draws, then
    /// magic_init() shuffles appearances inside a push/pop of the generator.
    ///
    /// The two state lines around magic_init are the interesting pair. The
    /// restore is deliberately inexact, so they should differ by exactly one -
    /// which is the quirk measured against the original rather than assumed.
    /// </summary>
    public static void DumpSeeds(TextWriter output, uint seed)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "seeds", seed);

        var game = new GameState();
        game.InitSeeds(seed);

        Line(output, "randes-seed", game.RandesSeed);
        Line(output, "town-seed", game.TownSeed);
        Line(output, "state-after-init-seeds", game.Rng.State);

        game.MagicInit();
        Line(output, "state-after-magic-init", game.Rng.State);

        // The shuffled tables are derived state in their own right, so dumping
        // them catches a divergence inside magic_init rather than only after it.
        Appearances appearances = game.Appearances;
        WriteNames(output, "color", appearances.Colors);
        WriteNames(output, "wood", appearances.Woods);
        WriteNames(output, "metal", appearances.Metals);
        WriteNames(output, "rock", appearances.Rocks);
        WriteNames(output, "amulet", appearances.Amulets);
        WriteNames(output, "mushroom", appearances.Mushrooms);
        WriteNames(output, "title", appearances.Titles);

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>
    /// The terrain primitives on their own: blank the cave, fill it with
    /// granite, drive the mineral veins through it, then wall the edges.
    ///
    /// Not a playable level - no rooms, no tunnels - but it exercises the layer
    /// of the generator that exists so far against a known generator state.
    /// </summary>
    public static void DumpStreamers(TextWriter output, uint seed, int level)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "streamers", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;

        game.Objects.Reset();
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();

        var generator = new DungeonGenerator(game);
        generator.FillCave(CaveFeature.GraniteWall);
        generator.PlaceStreamers();
        generator.PlaceBoundary();

        Cave cave = game.Cave;
        output.Write("height " + cave.Height.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("width " + cave.Width.ToString(CultureInfo.InvariantCulture) + "\n");

        var row = new char[cave.Width];
        for (int y = 0; y < cave.Height; y++)
        {
            for (int x = 0; x < cave.Width; x++)
            {
                row[x] = FeatureChar(cave[y, x].Feature);
            }

            output.Write(
                "row " + y.ToString(CultureInfo.InvariantCulture) + " " + new string(row) + "\n");
        }

        // No monster is placed this early, but the empty block is printed all
        // the same, so the two dumps line up.
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
                monster.Speed.ToString(CultureInfo.InvariantCulture),
                monster.Sleep.ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        int objectCount = game.Objects.Count - ObjectPool.FirstIndex;
        output.Write("objects " + objectCount.ToString(CultureInfo.InvariantCulture) + "\n");
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
                item.Cost.ToString(CultureInfo.InvariantCulture)) + "\n");
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

    /// <summary>
    /// One room builder, exercised over the whole grid of room slots the real
    /// generator would use.
    ///
    /// Rooms are placed at the coordinates cave_gen picks - the room grid is
    /// spaced half a screen apart - so the builders see realistic positions,
    /// while which builder runs stays fixed rather than being drawn. That keeps
    /// the comparison pointed at one function at a time.
    /// </summary>
    /// <param name="type">0 for the plain rectangle, 1 for overlapping ones.</param>
    public static void DumpRooms(TextWriter output, uint seed, int level, int type)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "rooms", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("type " + type.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;

        game.Objects.Reset();
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();

        var generator = new DungeonGenerator(game);
        Cave cave = game.Cave;

        // The room grid: half a screen apart, offset a quarter screen in.
        // SCREEN_HEIGHT and SCREEN_WIDTH are the viewport, which is also what
        // Umoria spaces rooms by.
        const int ScreenHeight = 22;
        const int ScreenWidth = 66;

        for (int i = 0; i < 2 * (cave.Height / ScreenHeight); i++)
        {
            for (int j = 0; j < 2 * (cave.Width / ScreenWidth); j++)
            {
                int row = (i * (ScreenHeight >> 1)) + (ScreenHeight / 4);
                int column = (j * (ScreenWidth >> 1)) + (ScreenWidth / 4);

                if (type == 0)
                {
                    generator.BuildRoom(row, column);
                }
                else
                {
                    generator.BuildOverlappingRoom(row, column);
                }
            }
        }

        generator.FillCave(CaveFeature.GraniteWall);
        generator.PlaceBoundary();

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

        // LitRoom marks a square as part of a room. Getting the shape right
        // while getting this wrong would leave rooms that never light up.
        for (int y = 0; y < cave.Height; y++)
        {
            int lit = 0;
            for (int x = 0; x < cave.Width; x++)
            {
                bool isLit = cave[y, x].LitRoom;
                line[x] = isLit ? 'L' : '.';
                if (isLit)
                {
                    lit++;
                }
            }

            if (lit > 0)
            {
                output.Write(
                    "lit " + y.ToString(CultureInfo.InvariantCulture)
                    + " " + new string(line) + "\n");
            }
        }

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>
    /// Rooms, then corridors joining them, then the junction doors.
    ///
    /// This is cave_gen's own order minus the streamers and stairs, which keeps
    /// the comparison on the tunneller. Rooms are built at fixed coordinates in
    /// fixed order - no shuffle - so which rooms get joined is not itself drawn
    /// from the generator, and a divergence points at the tunnel code rather
    /// than at the order it ran in.
    /// </summary>
    public static void DumpTunnels(TextWriter output, uint seed, int level)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "tunnels", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;

        game.Objects.Reset();
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();

        var generator = new DungeonGenerator(game);
        Cave cave = game.Cave;

        const int ScreenHeight = 22;
        const int ScreenWidth = 66;

        List<(int Row, int Column)> centres = [];
        for (int i = 0; i < 2 * (cave.Height / ScreenHeight); i++)
        {
            for (int j = 0; j < 2 * (cave.Width / ScreenWidth); j++)
            {
                int row = (i * (ScreenHeight >> 1)) + (ScreenHeight / 4);
                int column = (j * (ScreenWidth >> 1)) + (ScreenWidth / 4);
                centres.Add((row, column));
                generator.BuildRoom(row, column);
            }
        }

        generator.ResetDoorCandidates();

        // Join each room to the next, wrapping back to the first.
        for (int i = 0; i < centres.Count; i++)
        {
            (int fromRow, int fromColumn) = centres[(i + 1) % centres.Count];
            (int toRow, int toColumn) = centres[i];
            generator.BuildTunnel(fromRow, fromColumn, toRow, toColumn);
        }

        generator.FillCave(CaveFeature.GraniteWall);
        generator.PlaceBoundary();

        IReadOnlyList<(int Row, int Column)> junctions = generator.DoorCandidates;
        output.Write("junctions " + junctions.Count.ToString(CultureInfo.InvariantCulture) + "\n");
        for (int i = 0; i < junctions.Count; i++)
        {
            (int row, int column) = junctions[i];
            output.Write(string.Join(
                ' ',
                "junction",
                i.ToString(CultureInfo.InvariantCulture),
                row.ToString(CultureInfo.InvariantCulture),
                column.ToString(CultureInfo.InvariantCulture)) + "\n");

            generator.TryDoor(row, column - 1);
            generator.TryDoor(row, column + 1);
            generator.TryDoor(row - 1, column);
            generator.TryDoor(row + 1, column);
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

        // No monster is placed this early, but the empty block is printed all
        // the same, so the two dumps line up.
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
                monster.Speed.ToString(CultureInfo.InvariantCulture),
                monster.Sleep.ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        // Every door the tunneller left, with the p1 that separates locked from
        // stuck from broken.
        int objectCount = game.Objects.Count - ObjectPool.FirstIndex;
        output.Write("objects " + objectCount.ToString(CultureInfo.InvariantCulture) + "\n");
        for (int i = ObjectPool.FirstIndex; i < game.Objects.Count; i++)
        {
            InvenType item = game.Objects[i];
            output.Write(string.Join(
                ' ',
                "object",
                i.ToString(CultureInfo.InvariantCulture),
                item.Index.ToString(CultureInfo.InvariantCulture),
                item.TVal.ToString(CultureInfo.InvariantCulture),
                item.P1.ToString(CultureInfo.InvariantCulture)) + "\n");
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

    /// <summary>
    /// The whole terrain half of cave_gen: rooms, corridors, junction doors,
    /// granite fill, mineral veins, boundary, then staircases and the spot the
    /// player starts on.
    ///
    /// This is cave_gen exactly, stopping short of alloc_monster and
    /// alloc_object - everything that shapes the map, none of what populates
    /// it. Rooms are still built in fixed order so the comparison stays pointed
    /// at the terrain code rather than at which room type was drawn.
    /// </summary>
    public static void DumpStairs(TextWriter output, uint seed, int level)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "stairs", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;

        game.Objects.Reset();
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();

        var generator = new DungeonGenerator(game);
        Cave cave = game.Cave;

        const int ScreenHeight = 22;
        const int ScreenWidth = 66;

        List<(int Row, int Column)> centres = [];
        for (int i = 0; i < 2 * (cave.Height / ScreenHeight); i++)
        {
            for (int j = 0; j < 2 * (cave.Width / ScreenWidth); j++)
            {
                int row = (i * (ScreenHeight >> 1)) + (ScreenHeight / 4);
                int column = (j * (ScreenWidth >> 1)) + (ScreenWidth / 4);
                centres.Add((row, column));
                generator.BuildRoom(row, column);
            }
        }

        generator.ResetDoorCandidates();
        for (int i = 0; i < centres.Count; i++)
        {
            (int fromRow, int fromColumn) = centres[(i + 1) % centres.Count];
            (int toRow, int toColumn) = centres[i];
            generator.BuildTunnel(fromRow, fromColumn, toRow, toColumn);
        }

        generator.FillCave(CaveFeature.GraniteWall);
        generator.PlaceStreamers();
        generator.PlaceBoundary();
        generator.PlaceJunctionDoors();

        // Depth decides how much is scattered about; cave_gen clamps it to 2..10.
        int allocLevel = Math.Clamp(level / 3, 2, 10);
        output.Write("alloc-level " + allocLevel.ToString(CultureInfo.InvariantCulture) + "\n");

        generator.PlaceStairs(2, game.Rng.RandInt(2) + 2, 3);
        generator.PlaceStairs(1, game.Rng.RandInt(2), 3);

        (int charRow, int charColumn) = generator.NewSpot();
        output.Write("char-row " + charRow.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("char-col " + charColumn.ToString(CultureInfo.InvariantCulture) + "\n");

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

        // No monster is placed this early, but the empty block is printed all
        // the same, so the two dumps line up.
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
                monster.Speed.ToString(CultureInfo.InvariantCulture),
                monster.Sleep.ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        int objectCount = game.Objects.Count - ObjectPool.FirstIndex;
        output.Write("objects " + objectCount.ToString(CultureInfo.InvariantCulture) + "\n");
        for (int i = ObjectPool.FirstIndex; i < game.Objects.Count; i++)
        {
            InvenType item = game.Objects[i];
            output.Write(string.Join(
                ' ',
                "object",
                i.ToString(CultureInfo.InvariantCulture),
                item.Index.ToString(CultureInfo.InvariantCulture),
                item.TVal.ToString(CultureInfo.InvariantCulture),
                item.P1.ToString(CultureInfo.InvariantCulture)) + "\n");
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

    /// <summary>
    /// The object index and the draws that read it.
    ///
    /// get_obj_num picks items out of a table sorted by depth, so this dumps
    /// the sort itself and then a run of picks at the given level - both with
    /// and without the "must fit in a chest" restriction, since that path
    /// rejects and redraws.
    ///
    /// No enchantment happens here: magic_treasure is a separate layer. What is
    /// compared is which object was chosen, not what it was turned into.
    /// </summary>
    public static void DumpPicks(TextWriter output, uint seed, int level, int count)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "picks", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("count " + count.ToString(CultureInfo.InvariantCulture) + "\n");

        output.Write("max-obj-level "
            + ObjectLevels.MaxObjectLevel.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("dungeon-objects "
            + ObjectLevels.DungeonObjectCount.ToString(CultureInfo.InvariantCulture) + "\n");

        for (int i = 0; i <= ObjectLevels.MaxObjectLevel; i++)
        {
            output.Write("t-level " + i.ToString(CultureInfo.InvariantCulture)
                + " " + ObjectLevels.LevelTotals[i].ToString(CultureInfo.InvariantCulture) + "\n");
        }

        for (int i = 0; i < ObjectLevels.DungeonObjectCount; i++)
        {
            output.Write("sorted " + i.ToString(CultureInfo.InvariantCulture)
                + " " + ObjectLevels.Sorted[i].ToString(CultureInfo.InvariantCulture) + "\n");
        }

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;

        var generator = new DungeonGenerator(game);
        for (int i = 0; i < count; i++)
        {
            int any = generator.GetObjectNumber(level, mustBeSmall: false);
            int small = generator.GetObjectNumber(level, mustBeSmall: true);
            output.Write(string.Join(
                ' ',
                "pick",
                i.ToString(CultureInfo.InvariantCulture),
                any.ToString(CultureInfo.InvariantCulture),
                ObjectLevels.Sorted[any].ToString(CultureInfo.InvariantCulture),
                small.ToString(CultureInfo.InvariantCulture),
                ObjectLevels.Sorted[small].ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>
    /// Objects generated and enchanted at a given depth.
    ///
    /// magic_treasure is the largest function in Umoria and almost every branch
    /// ends in a different combination of bonuses, flags, charges and price, so
    /// the dump reports the whole item rather than a summary. Running it over
    /// many items at several depths reaches most of the switch: the chance of
    /// any magic at all, of something special, and of a curse all move with
    /// depth.
    /// </summary>
    public static void DumpEnchanted(TextWriter output, uint seed, int level, int count)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "enchanted", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("count " + count.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;
        game.Objects.Reset();

        var generator = new DungeonGenerator(game);

        // One slot, reused: each item is fully overwritten before enchanting,
        // and allocating a fresh one per item would exhaust the list.
        InvenType item = game.Objects[ObjectPool.FirstIndex];

        for (int i = 0; i < count; i++)
        {
            int pick = generator.GetObjectNumber(level, mustBeSmall: false);
            item.CopyFrom(ObjectLevels.Sorted[pick]);
            game.Enchantment.Apply(item, level);

            output.Write(string.Join(
                ' ',
                "item",
                i.ToString(CultureInfo.InvariantCulture),
                ObjectLevels.Sorted[pick].ToString(CultureInfo.InvariantCulture),
                item.TVal.ToString(CultureInfo.InvariantCulture),
                item.SubVal.ToString(CultureInfo.InvariantCulture),
                item.P1.ToString(CultureInfo.InvariantCulture),
                item.Cost.ToString(CultureInfo.InvariantCulture),
                item.Number.ToString(CultureInfo.InvariantCulture),
                item.Weight.ToString(CultureInfo.InvariantCulture),
                item.ToHit.ToString(CultureInfo.InvariantCulture),
                item.ToDam.ToString(CultureInfo.InvariantCulture),
                item.Ac.ToString(CultureInfo.InvariantCulture),
                item.ToAc.ToString(CultureInfo.InvariantCulture),
                item.Level.ToString(CultureInfo.InvariantCulture),
                item.Flags.ToString(CultureInfo.InvariantCulture)) + "\n");

            output.Write(string.Join(
                ' ',
                "item-extra",
                i.ToString(CultureInfo.InvariantCulture),
                item.SpecialName.ToString(CultureInfo.InvariantCulture),
                item.Identification.ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        output.Write("missile-counter "
            + game.MissileCounter.ToString(CultureInfo.InvariantCulture) + "\n");
        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>
    /// A finished level, less the monsters.
    ///
    /// This is cave_gen from end to end apart from alloc_monster and
    /// place_win_monster, which are not ported yet. Skipping them on both sides
    /// keeps the generator streams aligned, so what is compared is every object
    /// scattered across a real level: rubble in the corridors, treasure in the
    /// rooms, gold and traps anywhere.
    /// </summary>
    public static void DumpPopulate(TextWriter output, uint seed, int level)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "populate", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;
        game.Objects.Reset();
        game.Monsters.Reset();
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();

        var generator = new DungeonGenerator(game);
        Cave cave = game.Cave;

        const int ScreenHeight = 22;
        const int ScreenWidth = 66;

        List<(int Row, int Column)> centres = [];
        for (int i = 0; i < 2 * (cave.Height / ScreenHeight); i++)
        {
            for (int j = 0; j < 2 * (cave.Width / ScreenWidth); j++)
            {
                int row = (i * (ScreenHeight >> 1)) + (ScreenHeight / 4);
                int column = (j * (ScreenWidth >> 1)) + (ScreenWidth / 4);
                centres.Add((row, column));
                generator.BuildRoom(row, column);
            }
        }

        generator.ResetDoorCandidates();
        for (int i = 0; i < centres.Count; i++)
        {
            (int fromRow, int fromColumn) = centres[(i + 1) % centres.Count];
            (int toRow, int toColumn) = centres[i];
            generator.BuildTunnel(fromRow, fromColumn, toRow, toColumn);
        }

        generator.FillCave(CaveFeature.GraniteWall);
        generator.PlaceStreamers();
        generator.PlaceBoundary();
        generator.PlaceJunctionDoors();

        int allocLevel = Math.Clamp(level / 3, 2, 10);

        generator.PlaceStairs(2, game.Rng.RandInt(2) + 2, 3);
        generator.PlaceStairs(1, game.Rng.RandInt(2), 3);

        (int charRow, int charColumn) = generator.NewSpot();
        game.CharacterRow = charRow;
        game.CharacterColumn = charColumn;
        output.Write("char-row " + charRow.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("char-col " + charColumn.ToString(CultureInfo.InvariantCulture) + "\n");

        // MIN_MALLOC_LEVEL: the floor on how many monsters a level carries.
        const int MinimumMonsters = 14;
        generator.AllocMonster(
            game.Rng.RandInt(8) + MinimumMonsters + allocLevel,
            minimumDistance: 0,
            asleep: true);
        generator.PopulateLevel(allocLevel);

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
                monster.Speed.ToString(CultureInfo.InvariantCulture),
                monster.Sleep.ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        int objectCount = game.Objects.Count - ObjectPool.FirstIndex;
        output.Write("objects " + objectCount.ToString(CultureInfo.InvariantCulture) + "\n");
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
                item.P1.ToString(CultureInfo.InvariantCulture),
                item.Cost.ToString(CultureInfo.InvariantCulture),
                item.Number.ToString(CultureInfo.InvariantCulture),
                item.ToHit.ToString(CultureInfo.InvariantCulture),
                item.ToDam.ToString(CultureInfo.InvariantCulture),
                item.Flags.ToString(CultureInfo.InvariantCulture),
                item.SpecialName.ToString(CultureInfo.InvariantCulture)) + "\n");
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

    /// <summary>
    /// One character per terrain value, matching feature_char() in the C oracle.
    /// Every distinct value gets a distinct character so the dump stays exact
    /// while still being readable.
    /// </summary>
    internal static char FeatureChar(byte feature) => feature switch
    {
        CaveFeature.NullWall => ' ',
        CaveFeature.DarkFloor => '.',
        CaveFeature.LightFloor => ',',
        CaveFeature.CorridorFloor => '#',
        CaveFeature.BlockedFloor => '%',
        CaveFeature.Temp1Wall => '1',
        CaveFeature.Temp2Wall => '2',
        CaveFeature.GraniteWall => 'G',
        CaveFeature.MagmaWall => 'M',
        CaveFeature.QuartzWall => 'Q',
        CaveFeature.BoundaryWall => 'B',
        _ => '?',
    };

    internal static void Line(TextWriter output, string key, uint value) =>
        output.Write(key + " " + value.ToString(CultureInfo.InvariantCulture) + "\n");

    private static void WriteNames(TextWriter output, string key, string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            output.Write(
                key + " " + i.ToString(CultureInfo.InvariantCulture) + " " + names[i] + "\n");
        }
    }

    /// <summary>
    /// Runs one of the dump modes by name, matching the C oracle's command line.
    /// </summary>
    /// <returns>A process exit code: 0 on success, 2 on misuse.</returns>
    public static int Run(TextWriter output, TextWriter error, string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Length == 0)
        {
            return Usage(error);
        }

        switch (arguments[0])
        {
            case "rng":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint seed)
                    || !long.TryParse(arguments[2], CultureInfo.InvariantCulture, out long count))
                {
                    return Usage(error);
                }

                DumpRng(output, seed, count);
                return 0;

            // These wait on the code they exist to check. Reporting that plainly
            // beats emitting a dump that would silently compare nothing.
            case "seeds":
                if (arguments.Length != 2
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint seedsSeed))
                {
                    return Usage(error);
                }

                DumpSeeds(output, seedsSeed);
                return 0;

            case "streamers":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint streamSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int streamLevel))
                {
                    return Usage(error);
                }

                DumpStreamers(output, streamSeed, streamLevel);
                return 0;

            case "rooms":
                if (arguments.Length != 4
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint roomSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int roomLevel)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int roomType))
                {
                    return Usage(error);
                }

                DumpRooms(output, roomSeed, roomLevel, roomType);
                return 0;

            case "tunnels":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint tunSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int tunLevel))
                {
                    return Usage(error);
                }

                DumpTunnels(output, tunSeed, tunLevel);
                return 0;

            case "stairs":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint stSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int stLevel))
                {
                    return Usage(error);
                }

                DumpStairs(output, stSeed, stLevel);
                return 0;

            case "picks":
                if (arguments.Length != 4
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint pkSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int pkLevel)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int pkCount))
                {
                    return Usage(error);
                }

                DumpPicks(output, pkSeed, pkLevel, pkCount);
                return 0;

            case "enchanted":
                if (arguments.Length != 4
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint enSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int enLevel)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int enCount))
                {
                    return Usage(error);
                }

                DumpEnchanted(output, enSeed, enLevel, enCount);
                return 0;

            case "populate":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint poSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int poLevel))
                {
                    return Usage(error);
                }

                DumpPopulate(output, poSeed, poLevel);
                return 0;

            case "town":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint twSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int twTurn))
                {
                    return Usage(error);
                }

                DumpTown(output, twSeed, twTurn);
                return 0;

            case "shops":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint shSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int shRounds))
                {
                    return Usage(error);
                }

                DumpShops(output, shSeed, shRounds);
                return 0;

            case "character":
                if (arguments.Length != 5
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint chSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int chRace)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int chSex)
                    || !int.TryParse(arguments[4], CultureInfo.InvariantCulture, out int chClass))
                {
                    return Usage(error);
                }

                DumpCharacter(output, chSeed, chRace, chSex, chClass);
                return 0;

            case "screen":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint scSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int scLevel))
                {
                    return Usage(error);
                }

                DumpScreen(output, scSeed, scLevel);
                return 0;

            case "messages":
                if (arguments.Length != 2
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint msSeed))
                {
                    return Usage(error);
                }

                DumpMessages(output, msSeed);
                return 0;

            case "map":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint mpSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int mpLevel))
                {
                    return Usage(error);
                }

                DumpMap(output, mpSeed, mpLevel);
                return 0;

            case "scroll":
            case "wand":
            case "staff":
                if (arguments.Length != 5
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint devSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int devLevel)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int devFirst)
                    || !int.TryParse(arguments[4], CultureInfo.InvariantCulture, out int devCount))
                {
                    return Usage(error);
                }

                DumpDevice(output, arguments[0], devSeed, devLevel, devFirst, devCount);
                return 0;

            case "spell":
            case "prayer":
                if (arguments.Length != 5
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint magSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int magLevel)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int magFirst)
                    || !int.TryParse(arguments[4], CultureInfo.InvariantCulture, out int magCount))
                {
                    return Usage(error);
                }

                DumpMagic(output, arguments[0], magSeed, magLevel, magFirst, magCount);
                return 0;

            case "inven":
            case "getitem":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint invSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int invVariation))
                {
                    return Usage(error);
                }

                if (arguments[0] == "inven")
                {
                    DumpInventory(output, invSeed, invVariation);
                }
                else
                {
                    DumpItemPrompt(output, invSeed, invVariation);
                }

                return 0;

            case "moria4":
            case "look":
                if (arguments.Length != 4
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint m4Seed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int m4Level)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int m4Variation))
                {
                    return Usage(error);
                }

                if (arguments[0] == "moria4")
                {
                    DumpMoria4(output, m4Seed, m4Level, m4Variation);
                }
                else
                {
                    DumpLook(output, m4Seed, m4Level, m4Variation);
                }

                return 0;

            case "store":
                if (arguments.Length != 4
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint shopSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int stStore)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int stVariation))
                {
                    return Usage(error);
                }

                if (stStore < 0 || stStore >= StoreSets.StoreCount)
                {
                    error.WriteLine(
                        "airom: store " + stStore.ToString(CultureInfo.InvariantCulture)
                        + " is outside 0.."
                        + (StoreSets.StoreCount - 1).ToString(CultureInfo.InvariantCulture));

                    return 2;
                }

                DumpStore(output, shopSeed, stStore, stVariation);
                return 0;

            case "potion":
                if (arguments.Length != 4
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint potSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int potFirst)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int potCount))
                {
                    return Usage(error);
                }

                DumpPotion(output, potSeed, potFirst, potCount);
                return 0;

            case "monsters":
                if (arguments.Length != 5
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint mnSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int mnLevel)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int mnTurns)
                    || !int.TryParse(arguments[4], CultureInfo.InvariantCulture, out int mnVar))
                {
                    return Usage(error);
                }

                DumpMonsters(output, mnSeed, mnLevel, mnTurns, mnVar);
                return 0;

            case "fight":
                if (arguments.Length != 5
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint ftSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int ftLevel)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int ftCreature)
                    || !int.TryParse(arguments[4], CultureInfo.InvariantCulture, out int ftRounds))
                {
                    return Usage(error);
                }

                if (ftCreature < 0 || ftCreature >= GameTables.CreatureList.Length)
                {
                    // Said rather than thrown: an index past the creature table
                    // is a mistyped argument, and a stack trace says nothing
                    // about which one was wrong.
                    error.WriteLine(
                        "airom: creature " + ftCreature.ToString(CultureInfo.InvariantCulture)
                        + " is outside 0.."
                        + (GameTables.CreatureList.Length - 1)
                            .ToString(CultureInfo.InvariantCulture));

                    return 2;
                }

                DumpFight(output, ftSeed, ftLevel, ftCreature, ftRounds);
                return 0;

            case "traps":
                if (arguments.Length != 5
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint tpSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int tpLevel)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int tpFirst)
                    || !int.TryParse(arguments[4], CultureInfo.InvariantCulture, out int tpCount))
                {
                    return Usage(error);
                }

                DumpTraps(output, tpSeed, tpLevel, tpFirst, tpCount);
                return 0;

            case "pickup":
                if (arguments.Length != 5
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint pickSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int pickLevel)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int pickSteps)
                    || !int.TryParse(arguments[4], CultureInfo.InvariantCulture, out int pickVar))
                {
                    return Usage(error);
                }

                DumpPickup(output, pickSeed, pickLevel, pickSteps, pickVar);
                return 0;

            case "names":
                if (arguments.Length != 4
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint nmSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int nmFirst)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int nmCount))
                {
                    return Usage(error);
                }

                DumpNames(output, nmSeed, nmFirst, nmCount);
                return 0;

            case "search":
                if (arguments.Length != 5
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint srSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int srLevel)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int srRounds)
                    || !int.TryParse(arguments[4], CultureInfo.InvariantCulture, out int srChance))
                {
                    return Usage(error);
                }

                DumpSearch(output, srSeed, srLevel, srRounds, srChance);
                return 0;

            case "walk":
                if (arguments.Length != 5
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint wkSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int wkLevel)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int wkSteps)
                    || !int.TryParse(arguments[4], CultureInfo.InvariantCulture, out int wkVar))
                {
                    return Usage(error);
                }

                DumpWalk(output, wkSeed, wkLevel, wkSteps, wkVar);
                return 0;

            case "run":
                if (arguments.Length != 5
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint rnSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int rnLevel)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int rnDir)
                    || !int.TryParse(arguments[4], CultureInfo.InvariantCulture, out int rnVar))
                {
                    return Usage(error);
                }

                DumpRun(output, rnSeed, rnLevel, rnDir, rnVar);
                return 0;

            case "light":
                if (arguments.Length != 5
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint ltSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int ltLevel)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int ltSteps)
                    || !int.TryParse(arguments[4], CultureInfo.InvariantCulture, out int ltVar))
                {
                    return Usage(error);
                }

                DumpLight(output, ltSeed, ltLevel, ltSteps, ltVar);
                return 0;

            case "hallucinate":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint hlSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int hlLevel))
                {
                    return Usage(error);
                }

                DumpHallucinate(output, hlSeed, hlLevel);
                return 0;

            case "upkeep":
                if (arguments.Length != 4
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint upSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int upTurns)
                    || !int.TryParse(arguments[3], CultureInfo.InvariantCulture, out int upVar))
                {
                    return Usage(error);
                }

                DumpUpkeep(output, upSeed, upTurns, upVar);
                return 0;

            case "commands":
                if (arguments.Length != 1)
                {
                    return Usage(error);
                }

                DumpCommands(output);
                return 0;

            case "regen":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint rgSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int rgTurns))
                {
                    return Usage(error);
                }

                DumpRegen(output, rgSeed, rgTurns);
                return 0;

            case "statblock":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint sbSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int sbVar))
                {
                    return Usage(error);
                }

                DumpStatBlock(output, sbSeed, sbVar);
                return 0;

            case "cave":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint cvSeed)
                    || !int.TryParse(arguments[2], CultureInfo.InvariantCulture, out int cvLevel))
                {
                    return Usage(error);
                }

                DumpCave(output, cvSeed, cvLevel);
                return 0;

            default:
                return Usage(error);
        }
    }

    private static int Usage(TextWriter error)
    {
        error.WriteLine("usage:");
        error.WriteLine("  airom oracle rng   <seed> <count>   raw generator values");
        error.WriteLine("  airom oracle seeds <seed>           seeding chain and magic_init");
        error.WriteLine("  airom oracle cave  <seed> <level>   a generated dungeon level");
        error.WriteLine("  airom oracle streamers <seed> <level>  terrain primitives only");
        error.WriteLine("  airom oracle rooms <seed> <level> <type>  one room builder");
        error.WriteLine("  airom oracle tunnels <seed> <level>  rooms joined by corridors");
        error.WriteLine("  airom oracle stairs <seed> <level>  the whole terrain half of cave_gen");
        error.WriteLine("  airom oracle picks <seed> <level> <count>  object sort and get_obj_num");
        error.WriteLine("  airom oracle enchanted <seed> <level> <count>  magic_treasure");
        error.WriteLine("  airom oracle populate <seed> <level>  a finished level, less monsters");
        error.WriteLine("  airom oracle town <seed> <turn>  the town, less shop restocking");
        error.WriteLine("  airom oracle shops <seed> <rounds>  shop owners, stock and prices");
        error.WriteLine("  airom oracle character <seed> <race> <sex> <class>  a rolled character");
        error.WriteLine("  airom oracle screen <seed> <level>  the drawn map");
        error.WriteLine("  airom oracle messages <seed>  the message line and its history");
        error.WriteLine("  airom oracle statblock <seed> <variation>  the status sidebar");
        error.WriteLine("  airom oracle commands  the command translation and count tables");
        error.WriteLine("  airom oracle upkeep <seed> <turns> <variation>  a turn in the dungeon");
        error.WriteLine("  airom oracle hallucinate <seed> <level>  the map drawn while hallucinating");
        error.WriteLine("  airom oracle light <seed> <level> <steps> <variation>  a walk, lit");
        error.WriteLine("  airom oracle walk <seed> <level> <steps> <variation>  scripted steps");
        error.WriteLine("  airom oracle run <seed> <level> <direction> <variation>  one run");
        error.WriteLine("  airom oracle search <seed> <level> <rounds> <chance>  finding what is hidden");
        error.WriteLine("  airom oracle names <seed> <first> <count>  item descriptions");
        error.WriteLine("  airom oracle pickup <seed> <level> <steps> <variation>  carrying things");
        error.WriteLine("  airom oracle fight <seed> <level> <creature> <rounds>  hitting things");
        error.WriteLine("  airom oracle traps <seed> <level> <first> <count>  springing traps");
        error.WriteLine("  airom oracle monsters <seed> <level> <turns> <variation>  monster turns");
        error.WriteLine("  airom oracle potion <seed> <first> <count>  drinking things");
        error.WriteLine("  airom oracle scroll <seed> <level> <first> <count>  reading scrolls");
        error.WriteLine("  airom oracle wand <seed> <level> <first> <count>  aiming wands");
        error.WriteLine("  airom oracle staff <seed> <level> <first> <count>  using staffs");
        error.WriteLine("  airom oracle spell <seed> <level> <first> <count>  casting spells");
        error.WriteLine("  airom oracle prayer <seed> <level> <first> <count>  reciting prayers");
        error.WriteLine("  airom oracle inven <seed> <variation>  the inventory screens");
        error.WriteLine("  airom oracle getitem <seed> <variation>  the prompt that asks which item");
        error.WriteLine("  airom oracle moria4 <seed> <level> <variation>  digging, disarming, bashing, throwing");
        error.WriteLine("  airom oracle look <seed> <level> <variation>  the cone of peripheral vision");
        error.WriteLine("  airom oracle store <seed> <store> <variation>  a visit to a shop");
        error.WriteLine("  airom oracle regen <seed> <turns>  regeneration of hit points and mana");
        error.WriteLine("  airom oracle map <seed> <level>  the whole level shrunk to one screen");
        return 2;
    }
}
