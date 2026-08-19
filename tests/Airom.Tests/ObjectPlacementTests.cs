using Airom.Core;
using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on the object index, the depth-weighted draw that reads it, and the
/// two scattered objects that need no enchantment.
///
/// The sort and 100 draws per level are diffed against the C oracle across 49
/// seed and depth combinations, including level 0 and both sides of
/// MAX_OBJ_LEVEL. These pin the properties behind those numbers.
/// </summary>
public class ObjectPlacementTests
{
    private static (GameState Game, DungeonGenerator Generator) Prepared(uint seed, int level)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;
        game.Objects.Reset();
        game.Cave.Resize(66, 198);
        game.Cave.Blank();
        return (game, new DungeonGenerator(game));
    }

    // ------------------------------------------------------------- the index

    [Fact]
    public void ObjectLevels_IndexesEveryDungeonObjectExactlyOnce()
    {
        var seen = new HashSet<int>();
        foreach (int index in ObjectLevels.Sorted)
        {
            Assert.InRange(index, 0, ObjectLevels.DungeonObjectCount - 1);
            Assert.True(seen.Add(index), $"object {index} appears twice in the sort");
        }

        Assert.Equal(ObjectLevels.DungeonObjectCount, seen.Count);
    }

    /// <summary>
    /// The whole point of the index: everything up to a given total is at that
    /// level or shallower, so a draw bounded by the total can only return items
    /// legal for the depth.
    /// </summary>
    [Fact]
    public void ObjectLevels_SortsByDepth()
    {
        for (int level = 0; level <= ObjectLevels.MaxObjectLevel; level++)
        {
            for (int i = 0; i < ObjectLevels.LevelTotals[level]; i++)
            {
                byte objectLevel = GameTables.ObjectList[ObjectLevels.Sorted[i]].Level;
                Assert.True(
                    objectLevel <= level,
                    $"slot {i} holds a level {objectLevel} object inside the level {level} range");
            }
        }
    }

    [Fact]
    public void ObjectLevels_TotalsAreNonDecreasingAndCoverEverything()
    {
        for (int level = 1; level <= ObjectLevels.MaxObjectLevel; level++)
        {
            Assert.True(
                ObjectLevels.LevelTotals[level] >= ObjectLevels.LevelTotals[level - 1],
                $"totals fell between level {level - 1} and {level}");
        }

        Assert.Equal(
            ObjectLevels.DungeonObjectCount,
            ObjectLevels.LevelTotals[ObjectLevels.MaxObjectLevel]);
    }

    // -------------------------------------------------------------- the draw

    [Fact]
    public void GetObjectNumber_ReturnsAValidIndex()
    {
        (_, DungeonGenerator generator) = Prepared(12345, 10);

        for (int i = 0; i < 500; i++)
        {
            int index = generator.GetObjectNumber(10, mustBeSmall: false);
            Assert.InRange(index, 0, ObjectLevels.DungeonObjectCount - 1);
        }
    }

    /// <summary>
    /// The town uses level 0, which takes a separate branch drawing only from
    /// the shallowest items.
    /// </summary>
    [Fact]
    public void GetObjectNumber_AtLevelZero_DrawsOnlyFromTheShallowestItems()
    {
        (_, DungeonGenerator generator) = Prepared(4242, 0);

        for (int i = 0; i < 300; i++)
        {
            int index = generator.GetObjectNumber(0, mustBeSmall: false);
            Assert.InRange(index, 0, ObjectLevels.LevelTotals[0] - 1);
            Assert.Equal(0, GameTables.ObjectList[ObjectLevels.Sorted[index]].Level);
        }
    }

    /// <summary>
    /// The small-item path rejects and redraws until it lands on something that
    /// would fit in a chest, so it must never return a bulky item.
    /// </summary>
    [Fact]
    public void GetObjectNumber_WhenSmallIsRequired_NeverReturnsSomethingBulky()
    {
        for (uint seed = 1; seed <= 10; seed++)
        {
            (_, DungeonGenerator generator) = Prepared(seed, 25);

            for (int i = 0; i < 200; i++)
            {
                int index = generator.GetObjectNumber(25, mustBeSmall: true);
                TreasureType item = GameTables.ObjectList[ObjectLevels.Sorted[index]];
                Assert.False(
                    ItemSets.IsTooLargeForChest(item),
                    $"seed {seed}: chest draw returned {item.Name}, which is too large");
            }
        }
    }

    /// <summary>
    /// Deeper levels should yield deeper items on average. The distribution is
    /// deliberately skewed towards the depth cap rather than uniform, so this is
    /// a coarse check that the skew points the right way.
    /// </summary>
    [Fact]
    public void GetObjectNumber_DrawsDeeperItemsAtDepth()
    {
        double shallow = AverageLevel(5);
        double deep = AverageLevel(40);

        Assert.True(deep > shallow, $"level 40 averaged {deep}, level 5 averaged {shallow}");
    }

    private static double AverageLevel(int level)
    {
        (_, DungeonGenerator generator) = Prepared(999, level);

        long total = 0;
        const int Samples = 2000;
        for (int i = 0; i < Samples; i++)
        {
            int index = generator.GetObjectNumber(level, mustBeSmall: false);
            total += GameTables.ObjectList[ObjectLevels.Sorted[index]].Level;
        }

        return (double)total / Samples;
    }

    /// <summary>
    /// Depth is clamped at MAX_OBJ_LEVEL, so a level past the cap behaves like
    /// the cap rather than indexing off the end.
    /// </summary>
    [Fact]
    public void GetObjectNumber_ClampsDepthAtTheIndexCeiling()
    {
        (_, DungeonGenerator generator) = Prepared(777, 200);

        for (int i = 0; i < 200; i++)
        {
            int index = generator.GetObjectNumber(200, mustBeSmall: false);
            Assert.InRange(index, 0, ObjectLevels.DungeonObjectCount - 1);
        }
    }

    // ------------------------------------------------------ traps and rubble

    [Fact]
    public void PlaceTrap_PutsATrapOnTheSquare()
    {
        (GameState game, DungeonGenerator generator) = Prepared(12345, 10);
        game.Cave[10, 10].Feature = CaveFeature.CorridorFloor;

        generator.PlaceTrap(10, 10, 0);

        int index = game.Cave[10, 10].ObjectIndex;
        Assert.NotEqual(0, index);
        Assert.Equal(378, game.Objects[index].Index); // OBJ_TRAP_LIST
        Assert.Equal(CaveFeature.CorridorFloor, game.Cave[10, 10].Feature); // unchanged
    }

    [Fact]
    public void PlaceRandomTrap_StaysWithinTheTrapRows()
    {
        (GameState game, DungeonGenerator generator) = Prepared(4242, 10);

        for (int i = 0; i < 18; i++)
        {
            generator.PlaceRandomTrap(20, 20 + i);
            int index = game.Cave[20, 20 + i].ObjectIndex;
            Assert.InRange(game.Objects[index].Index, 378, 378 + 17); // MAX_TRAP rows
        }
    }

    /// <summary>
    /// Rubble is the one scattered object that also changes the terrain under
    /// it - a trap leaves the floor walkable, rubble blocks it.
    /// </summary>
    [Fact]
    public void PlaceRubble_BlocksTheSquareUnlikeATrap()
    {
        (GameState game, DungeonGenerator generator) = Prepared(999, 10);
        game.Cave[15, 15].Feature = CaveFeature.CorridorFloor;
        game.Cave[15, 16].Feature = CaveFeature.CorridorFloor;

        generator.PlaceRubble(15, 15);
        generator.PlaceTrap(15, 16, 3);

        Assert.Equal(CaveFeature.BlockedFloor, game.Cave[15, 15].Feature);
        Assert.Equal(CaveFeature.CorridorFloor, game.Cave[15, 16].Feature);
        Assert.Equal(396, game.Objects[game.Cave[15, 15].ObjectIndex].Index); // OBJ_RUBBLE
    }
}
