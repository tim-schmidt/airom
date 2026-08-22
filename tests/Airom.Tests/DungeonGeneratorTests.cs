using Airom.Core;
using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on the terrain layer of the dungeon generator.
///
/// The full output is diffed against the C oracle across 45 seed and depth
/// combinations. These pin the structural properties that would otherwise fail
/// as an unreadable wall of grid differences.
/// </summary>
public class DungeonGeneratorTests
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

    [Fact]
    public void Cave_StartsBlankAtTheDungeonSize()
    {
        var cave = new Cave(GameState.DungeonHeight, GameState.DungeonWidth);

        Assert.Equal(66, cave.Height);
        Assert.Equal(198, cave.Width);
        Assert.Equal(CaveFeature.NullWall, cave[0, 0].Feature);
        Assert.Equal(0, cave[30, 100].ObjectIndex);
    }

    /// <summary>
    /// in_bounds() excludes the outer ring rather than merely checking the array
    /// range, because that ring is the indestructible boundary wall.
    /// </summary>
    [Fact]
    public void InBounds_ExcludesTheBoundaryRing()
    {
        var cave = new Cave(66, 198);

        Assert.False(cave.InBounds(0, 50));
        Assert.False(cave.InBounds(65, 50));
        Assert.False(cave.InBounds(30, 0));
        Assert.False(cave.InBounds(30, 197));
        Assert.True(cave.InBounds(1, 1));
        Assert.True(cave.InBounds(64, 196));
    }

    /// <summary>
    /// mmove() checks the whole array rather than in_bounds: the streamer walk
    /// relies on stepping onto the boundary ring, and only stops past the edge.
    /// </summary>
    [Fact]
    public void Move_StepsByKeypadDirectionAndStopsAtTheEdge()
    {
        var cave = new Cave(66, 198);

        int row = 30;
        int column = 100;

        Assert.True(cave.Move(8, ref row, ref column)); // up
        Assert.Equal(29, row);
        Assert.Equal(100, column);

        Assert.True(cave.Move(3, ref row, ref column)); // down and right
        Assert.Equal(30, row);
        Assert.Equal(101, column);

        row = 0;
        column = 100;
        Assert.False(cave.Move(8, ref row, ref column)); // off the top
        Assert.Equal(0, row); // position left untouched
        Assert.Equal(100, column);
    }

    [Fact]
    public void FillCave_FillsEmptyAndScratchSquaresButNotTheBorder()
    {
        var game = new GameState();
        game.Cave.Resize(66, 198);
        game.Cave[10, 10].Feature = CaveFeature.Temp1Wall;
        game.Cave[10, 11].Feature = CaveFeature.DarkFloor;

        new DungeonGenerator(game).FillCave(CaveFeature.GraniteWall);

        Assert.Equal(CaveFeature.GraniteWall, game.Cave[10, 10].Feature); // scratch filled
        Assert.Equal(CaveFeature.DarkFloor, game.Cave[10, 11].Feature);   // floor kept
        Assert.Equal(CaveFeature.NullWall, game.Cave[0, 0].Feature);      // border skipped
    }

    [Fact]
    public void PlaceBoundary_RingsTheWholeLevel()
    {
        var game = new GameState();
        game.Cave.Resize(66, 198);

        new DungeonGenerator(game).PlaceBoundary();

        for (int row = 0; row < game.Cave.Height; row++)
        {
            Assert.Equal(CaveFeature.BoundaryWall, game.Cave[row, 0].Feature);
            Assert.Equal(CaveFeature.BoundaryWall, game.Cave[row, 197].Feature);
        }

        for (int column = 0; column < game.Cave.Width; column++)
        {
            Assert.Equal(CaveFeature.BoundaryWall, game.Cave[0, column].Feature);
            Assert.Equal(CaveFeature.BoundaryWall, game.Cave[65, column].Feature);
        }

        Assert.NotEqual(CaveFeature.BoundaryWall, game.Cave[33, 99].Feature);
    }

    /// <summary>
    /// A streamer only converts granite, so it can never eat through a room or
    /// a corridor carved before it.
    /// </summary>
    [Fact]
    public void PlaceStreamer_OnlyConvertsGranite()
    {
        (GameState game, DungeonGenerator generator) = Prepared(4242, 10);
        generator.FillCave(CaveFeature.GraniteWall);

        for (int row = 20; row < 30; row++)
        {
            for (int column = 60; column < 90; column++)
            {
                game.Cave[row, column].Feature = CaveFeature.LightFloor;
            }
        }

        generator.PlaceStreamers();

        for (int row = 20; row < 30; row++)
        {
            for (int column = 60; column < 90; column++)
            {
                Assert.Equal(CaveFeature.LightFloor, game.Cave[row, column].Feature);
            }
        }
    }

    [Fact]
    public void PlaceStreamers_LeaveBothMineralsInTheLevel()
    {
        (GameState game, DungeonGenerator generator) = Prepared(12345, 10);
        generator.FillCave(CaveFeature.GraniteWall);
        generator.PlaceStreamers();

        int magma = 0;
        int quartz = 0;
        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                byte feature = game.Cave[row, column].Feature;
                if (feature == CaveFeature.MagmaWall)
                {
                    magma++;
                }
                else if (feature == CaveFeature.QuartzWall)
                {
                    quartz++;
                }
            }
        }

        Assert.True(magma > 0, "no magma was placed");
        Assert.True(quartz > 0, "no quartz was placed");
    }

    /// <summary>
    /// Gold in a vein has to be reachable both ways: the square points at the
    /// object, and the object is a real gold row from the table.
    /// </summary>
    [Fact]
    public void PlaceStreamers_DropGoldThatTheCaveAndListAgreeOn()
    {
        (GameState game, DungeonGenerator generator) = Prepared(12345, 10);
        generator.FillCave(CaveFeature.GraniteWall);
        generator.PlaceStreamers();

        int placed = 0;
        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                int index = game.Cave[row, column].ObjectIndex;
                if (index == 0)
                {
                    continue;
                }

                placed++;
                Assert.InRange(index, ObjectPool.FirstIndex, game.Objects.Count - 1);
                Assert.Equal(ItemCategory.Gold, game.Objects[index].TVal);
            }
        }

        Assert.True(placed > 0, "no gold was placed in any vein");
        Assert.Equal(game.Objects.Count - ObjectPool.FirstIndex, placed);
    }

    [Fact]
    public void Generation_IsReproducibleForASeed()
    {
        (GameState first, DungeonGenerator firstGenerator) = Prepared(777, 20);
        firstGenerator.FillCave(CaveFeature.GraniteWall);
        firstGenerator.PlaceStreamers();

        (GameState second, DungeonGenerator secondGenerator) = Prepared(777, 20);
        secondGenerator.FillCave(CaveFeature.GraniteWall);
        secondGenerator.PlaceStreamers();

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

    // ------------------------------------------------------------ object pool

    [Fact]
    public void ObjectPool_StartsAtIndexOneBecauseZeroMeansNothing()
    {
        var pool = new ObjectPool();

        Assert.Equal(1, ObjectPool.FirstIndex);
        Assert.Equal(ObjectPool.FirstIndex, pool.Count);
    }

    [Fact]
    public void ObjectPool_AllocateHandsOutConsecutiveSlots()
    {
        var pool = new ObjectPool();

        Assert.Equal(1, pool.Allocate());
        Assert.Equal(2, pool.Allocate());
        Assert.Equal(3, pool.Count);
    }

    /// <summary>
    /// Squares refer to items by index, so releasing one moves the last item
    /// down into the gap and repoints the square that held it. Leaving a hole
    /// would strand every square pointing above it.
    /// </summary>
    [Fact]
    public void ObjectPool_ReleaseMovesTheLastItemAndRepointsItsSquare()
    {
        var cave = new Cave(20, 20);
        var pool = new ObjectPool();

        int first = pool.Allocate();
        int last = pool.Allocate();
        pool[first].CopyFrom(399);
        pool[last].CopyFrom(400);
        cave[5, 5].ObjectIndex = first;
        cave[9, 9].ObjectIndex = last;

        pool.Release(first, cave);

        Assert.Equal(400, pool[first].Index);        // last item moved down
        Assert.Equal(first, cave[9, 9].ObjectIndex); // its square repointed
        Assert.Equal(ObjectPool.FirstIndex + 1, pool.Count);
    }

    [Fact]
    public void InvenType_CopyFromStampsTheTemplate()
    {
        var item = new InvenType();
        item.CopyFrom(0);

        TreasureType template = GameTables.ObjectList[0];
        Assert.Equal(0, item.Index);
        Assert.Equal(template.TVal, item.TVal);
        Assert.Equal(template.Cost, item.Cost);
        Assert.Equal(template.Flags, item.Flags);
        Assert.Equal(template.Weight, item.Weight);
        Assert.Equal(0, item.Identification);
    }
}
