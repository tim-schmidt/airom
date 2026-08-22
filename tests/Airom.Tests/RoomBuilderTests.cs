using Airom.Core;
using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on the room builders.
///
/// The full grid is diffed against the C oracle across 84 seed, depth and
/// builder combinations. These pin the structural properties, which a grid diff
/// reports only as an unreadable wall of differences.
/// </summary>
public class RoomBuilderTests
{
    private static (GameState Game, DungeonGenerator Generator) Prepared(uint seed, int level)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;
        game.Objects.Reset();
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();
        return (game, new DungeonGenerator(game));
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
    public void BuildRoom_CarvesFloorSurroundedByGranite()
    {
        (GameState game, DungeonGenerator generator) = Prepared(12345, 5);

        generator.BuildRoom(33, 99);

        Cave cave = game.Cave;
        int floor = CountFeature(cave, CaveFeature.LightFloor)
            + CountFeature(cave, CaveFeature.DarkFloor);

        Assert.True(floor > 0, "the room has no floor");
        Assert.True(CountFeature(cave, CaveFeature.GraniteWall) > 0, "the room has no wall");
    }

    /// <summary>
    /// Every floor square must have a wall between it and the untouched rock,
    /// or a room would open straight into the fill.
    /// </summary>
    [Fact]
    public void BuildRoom_LeavesNoFloorTouchingUncarvedRock()
    {
        (GameState game, DungeonGenerator generator) = Prepared(777, 5);
        generator.BuildRoom(33, 99);

        Cave cave = game.Cave;
        for (int row = 1; row < cave.Height - 1; row++)
        {
            for (int column = 1; column < cave.Width - 1; column++)
            {
                if (!CaveSets.IsRoom(cave[row, column].Feature))
                {
                    continue;
                }

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        Assert.NotEqual(
                            CaveFeature.NullWall,
                            cave[row + dy, column + dx].Feature);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Room squares carry the lit-room mark, which is what makes the whole room
    /// light up as one piece when the player walks in. Getting the shape right
    /// while missing this would leave rooms permanently dark.
    /// </summary>
    [Fact]
    public void BuildRoom_MarksEveryCarvedSquareAsPartOfARoom()
    {
        (GameState game, DungeonGenerator generator) = Prepared(4242, 5);
        generator.BuildRoom(33, 99);

        Cave cave = game.Cave;
        for (int row = 0; row < cave.Height; row++)
        {
            for (int column = 0; column < cave.Width; column++)
            {
                if (cave[row, column].Feature != CaveFeature.NullWall)
                {
                    Assert.True(
                        cave[row, column].LitRoom,
                        $"({row},{column}) was carved but not marked as room");
                }
            }
        }
    }

    /// <summary>
    /// Depth decides lighting: at level 1 a room is almost always lit, and past
    /// level 25 the draw cannot succeed, so every room is dark.
    /// </summary>
    [Fact]
    public void BuildRoom_DeepRoomsAreAlwaysDark()
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            (GameState game, DungeonGenerator generator) = Prepared(seed, 26);
            generator.BuildRoom(33, 99);

            Assert.Equal(0, CountFeature(game.Cave, CaveFeature.LightFloor));
            Assert.True(CountFeature(game.Cave, CaveFeature.DarkFloor) > 0);
        }
    }

    [Fact]
    public void BuildRoom_ShallowRoomsAreUsuallyLit()
    {
        int lit = 0;
        for (uint seed = 1; seed <= 20; seed++)
        {
            (GameState game, DungeonGenerator generator) = Prepared(seed, 1);
            generator.BuildRoom(33, 99);

            if (CountFeature(game.Cave, CaveFeature.LightFloor) > 0)
            {
                lit++;
            }
        }

        Assert.True(lit >= 18, $"only {lit} of 20 shallow rooms were lit");
    }

    /// <summary>
    /// The room sits inside the extents the four draws allow: at most four rows
    /// up, three down and eleven columns either side of the centre, plus a wall.
    /// </summary>
    [Fact]
    public void BuildRoom_StaysWithinItsDrawnExtents()
    {
        const int CentreRow = 33;
        const int CentreColumn = 99;

        (GameState game, DungeonGenerator generator) = Prepared(31337, 5);
        generator.BuildRoom(CentreRow, CentreColumn);

        Cave cave = game.Cave;
        for (int row = 0; row < cave.Height; row++)
        {
            for (int column = 0; column < cave.Width; column++)
            {
                if (cave[row, column].Feature == CaveFeature.NullWall)
                {
                    continue;
                }

                Assert.InRange(row, CentreRow - 5, CentreRow + 4);
                Assert.InRange(column, CentreColumn - 12, CentreColumn + 12);
            }
        }
    }

    /// <summary>
    /// The overlapping builder lays walls only where there is no floor, so its
    /// two or three rectangles join into one space instead of bricking each
    /// other off. A wall stranded inside the floor would mean that check failed.
    /// </summary>
    [Fact]
    public void BuildOverlappingRoom_DoesNotWallOffItsOwnInterior()
    {
        for (uint seed = 1; seed <= 30; seed++)
        {
            (GameState game, DungeonGenerator generator) = Prepared(seed, 5);
            generator.BuildOverlappingRoom(33, 99);

            Cave cave = game.Cave;
            for (int row = 1; row < cave.Height - 1; row++)
            {
                for (int column = 1; column < cave.Width - 1; column++)
                {
                    if (cave[row, column].Feature != CaveFeature.GraniteWall)
                    {
                        continue;
                    }

                    // A wall fully enclosed by floor would be unreachable rock
                    // left behind in the middle of the room.
                    bool allFloor =
                        CaveSets.IsRoom(cave[row - 1, column].Feature)
                        && CaveSets.IsRoom(cave[row + 1, column].Feature)
                        && CaveSets.IsRoom(cave[row, column - 1].Feature)
                        && CaveSets.IsRoom(cave[row, column + 1].Feature);

                    Assert.False(
                        allFloor,
                        $"seed {seed}: wall at ({row},{column}) is surrounded by floor");
                }
            }
        }
    }

    [Fact]
    public void BuildOverlappingRoom_CarvesAtLeastAsMuchAsAPlainRoom()
    {
        (GameState plainGame, DungeonGenerator plain) = Prepared(2024, 5);
        plain.BuildRoom(33, 99);
        int plainFloor = CountFeature(plainGame.Cave, CaveFeature.LightFloor)
            + CountFeature(plainGame.Cave, CaveFeature.DarkFloor);

        (GameState overlapGame, DungeonGenerator overlapping) = Prepared(2024, 5);
        overlapping.BuildOverlappingRoom(33, 99);
        int overlapFloor = CountFeature(overlapGame.Cave, CaveFeature.LightFloor)
            + CountFeature(overlapGame.Cave, CaveFeature.DarkFloor);

        Assert.True(
            overlapFloor >= plainFloor,
            $"overlapping room carved {overlapFloor}, plain carved {plainFloor}");
    }

    [Fact]
    public void RoomBuilders_AreReproducibleForASeed()
    {
        (GameState first, DungeonGenerator firstGenerator) = Prepared(555, 10);
        firstGenerator.BuildOverlappingRoom(33, 99);

        (GameState second, DungeonGenerator secondGenerator) = Prepared(555, 10);
        secondGenerator.BuildOverlappingRoom(33, 99);

        Assert.Equal(first.Rng.State, second.Rng.State);
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
