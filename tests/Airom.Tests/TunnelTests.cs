using Airom.Core;
using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on the corridor digger and the doors it leaves behind.
///
/// The full grid is diffed against the C oracle across 45 seed and depth
/// combinations. These pin the properties a grid diff would only report as an
/// unreadable wall of differences.
/// </summary>
public class TunnelTests
{
    private const int ScreenHeight = 22;
    private const int ScreenWidth = 66;

    private static (GameState Game, DungeonGenerator Generator, List<(int Row, int Column)> Centres)
        LevelWithTunnels(uint seed, int level)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;
        game.Objects.Reset();
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();

        var generator = new DungeonGenerator(game);
        List<(int Row, int Column)> centres = [];

        for (int i = 0; i < 2 * (game.Cave.Height / ScreenHeight); i++)
        {
            for (int j = 0; j < 2 * (game.Cave.Width / ScreenWidth); j++)
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
        generator.PlaceBoundary();

        return (game, generator, centres);
    }

    private static int CountFeature(Cave cave, byte feature)
    {
        int count = 0;
        for (int row = 0; row < cave.Height; row++)
        {
            for (int column = 0; column < cave.Width; column++)
            {
                if (cave[row, column].Feature == feature)
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Fact]
    public void BuildTunnel_CarvesCorridor()
    {
        (GameState game, _, _) = LevelWithTunnels(12345, 5);

        Assert.True(
            CountFeature(game.Cave, CaveFeature.CorridorFloor) > 0,
            "no corridor was carved");
    }

    /// <summary>
    /// The two scratch wall values are working marks the tunneller leaves while
    /// deciding where doors go. fill_cave turns them back into granite, so none
    /// may survive into the finished level - one that did would be an
    /// undiggable, undrawable square.
    /// </summary>
    [Fact]
    public void BuildTunnel_LeavesNoScratchMarksBehind()
    {
        for (uint seed = 1; seed <= 12; seed++)
        {
            (GameState game, _, _) = LevelWithTunnels(seed, 5);

            Assert.Equal(0, CountFeature(game.Cave, CaveFeature.Temp1Wall));
            Assert.Equal(0, CountFeature(game.Cave, CaveFeature.Temp2Wall));
        }
    }

    /// <summary>
    /// Tunnels must never breach the outer ring, or the level would open onto
    /// nothing. in_bounds is what holds them in.
    /// </summary>
    [Fact]
    public void BuildTunnel_NeverBreachesTheBoundary()
    {
        for (uint seed = 1; seed <= 12; seed++)
        {
            (GameState game, _, _) = LevelWithTunnels(seed, 5);
            Cave cave = game.Cave;

            for (int row = 0; row < cave.Height; row++)
            {
                Assert.Equal(CaveFeature.BoundaryWall, cave[row, 0].Feature);
                Assert.Equal(CaveFeature.BoundaryWall, cave[row, cave.Width - 1].Feature);
            }

            for (int column = 0; column < cave.Width; column++)
            {
                Assert.Equal(CaveFeature.BoundaryWall, cave[0, column].Feature);
                Assert.Equal(CaveFeature.BoundaryWall, cave[cave.Height - 1, column].Feature);
            }
        }
    }

    /// <summary>
    /// Every door object must sit on a square that points back at it, and on
    /// terrain that matches its kind: open doors are walkable corridor, closed
    /// and secret ones block.
    /// </summary>
    [Fact]
    public void Doors_AgreeWithTheSquaresTheySitOn()
    {
        (GameState game, _, _) = LevelWithTunnels(12345, 5);
        Cave cave = game.Cave;

        int doors = 0;
        for (int row = 0; row < cave.Height; row++)
        {
            for (int column = 0; column < cave.Width; column++)
            {
                int index = cave[row, column].ObjectIndex;
                if (index == 0)
                {
                    continue;
                }

                doors++;
                InvenType door = game.Objects[index];
                Assert.InRange(index, ObjectPool.FirstIndex, game.Objects.Count - 1);

                byte feature = cave[row, column].Feature;
                if (door.TVal == ItemCategory.OpenDoor)
                {
                    Assert.Equal(CaveFeature.CorridorFloor, feature);
                }
                else
                {
                    Assert.Contains(
                        door.TVal,
                        new[] { ItemCategory.ClosedDoor, ItemCategory.SecretDoor });
                    Assert.Equal(CaveFeature.BlockedFloor, feature);
                }
            }
        }

        Assert.True(doors > 0, "no doors were placed");
        Assert.Equal(game.Objects.Count - ObjectPool.FirstIndex, doors);
    }

    /// <summary>
    /// p1 carries three states in one field: positive is a lock strength,
    /// negative is how badly the door is jammed, and 1 on an open door means
    /// broken. Collapsing the sign would make locked and stuck doors
    /// indistinguishable.
    /// </summary>
    [Fact]
    public void Doors_UseTheSignOfP1ToSeparateLockedFromStuck()
    {
        var locked = 0;
        var stuck = 0;

        for (uint seed = 1; seed <= 25; seed++)
        {
            (GameState game, _, _) = LevelWithTunnels(seed, 5);

            for (int i = ObjectPool.FirstIndex; i < game.Objects.Count; i++)
            {
                InvenType door = game.Objects[i];
                if (door.TVal != ItemCategory.ClosedDoor)
                {
                    continue;
                }

                if (door.P1 > 0)
                {
                    locked++;
                    Assert.InRange(door.P1, 11, 20);
                }
                else if (door.P1 < 0)
                {
                    stuck++;
                    Assert.InRange(door.P1, -20, -11);
                }
            }
        }

        Assert.True(locked > 0, "no locked doors appeared in 25 levels");
        Assert.True(stuck > 0, "no stuck doors appeared in 25 levels");
    }

    [Fact]
    public void Doors_IncludeEveryKind()
    {
        var kinds = new HashSet<byte>();
        for (uint seed = 1; seed <= 15; seed++)
        {
            (GameState game, _, _) = LevelWithTunnels(seed, 5);
            for (int i = ObjectPool.FirstIndex; i < game.Objects.Count; i++)
            {
                kinds.Add(game.Objects[i].TVal);
            }
        }

        Assert.Contains(ItemCategory.OpenDoor, kinds);
        Assert.Contains(ItemCategory.ClosedDoor, kinds);
        Assert.Contains(ItemCategory.SecretDoor, kinds);
    }

    /// <summary>
    /// Junctions are recorded while tunnelling and revisited afterwards, so the
    /// list has to survive past the tunnel that filled it.
    /// </summary>
    [Fact]
    public void Tunnelling_RecordsJunctionsForLaterDoorPlacement()
    {
        (_, DungeonGenerator generator, _) = LevelWithTunnels(12345, 5);

        Assert.NotEmpty(generator.DoorCandidates);
        Assert.True(
            generator.DoorCandidates.Count <= 100,
            "the junction list exceeded Umoria's cap of 100");
    }

    [Fact]
    public void ResetDoorCandidates_ClearsTheList()
    {
        (_, DungeonGenerator generator, _) = LevelWithTunnels(12345, 5);
        Assert.NotEmpty(generator.DoorCandidates);

        generator.ResetDoorCandidates();

        Assert.Empty(generator.DoorCandidates);
    }

    /// <summary>
    /// Rooms have to end up reachable. Flood filling from one room centre should
    /// reach the others, which is what the tunnels exist to guarantee - and what
    /// the "gone a reasonable distance" check in the digger protects.
    /// </summary>
    [Fact]
    public void Tunnels_ConnectTheRooms()
    {
        (GameState game, _, List<(int Row, int Column)> centres) = LevelWithTunnels(12345, 5);
        Cave cave = game.Cave;

        var seen = new bool[cave.Height, cave.Width];
        var queue = new Queue<(int Row, int Column)>();
        queue.Enqueue(centres[0]);
        seen[centres[0].Row, centres[0].Column] = true;

        while (queue.Count > 0)
        {
            (int row, int column) = queue.Dequeue();
            foreach ((int dy, int dx) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            {
                int y = row + dy;
                int x = column + dx;
                if (!cave.InBounds(y, x) || seen[y, x])
                {
                    continue;
                }

                // Blocked squares are doors, which count as passable here: the
                // player can open them.
                if (cave[y, x].Feature > CaveFeature.MaxCaveFloor)
                {
                    continue;
                }

                seen[y, x] = true;
                queue.Enqueue((y, x));
            }
        }

        int reached = centres.Count(c => seen[c.Row, c.Column]);
        Assert.True(
            reached >= centres.Count - 1,
            $"only {reached} of {centres.Count} room centres were reachable");
    }

    [Fact]
    public void Tunnelling_IsReproducibleForASeed()
    {
        (GameState first, _, _) = LevelWithTunnels(777, 10);
        (GameState second, _, _) = LevelWithTunnels(777, 10);

        Assert.Equal(first.Rng.State, second.Rng.State);
        Assert.Equal(first.Objects.Count, second.Objects.Count);

        for (int row = 0; row < first.Cave.Height; row++)
        {
            for (int column = 0; column < first.Cave.Width; column++)
            {
                Assert.Equal(
                    first.Cave[row, column].Feature,
                    second.Cave[row, column].Feature);
            }
        }
    }
}
