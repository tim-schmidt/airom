using Airom.Core;
using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on scattering objects across a finished level.
///
/// Whole populated levels are diffed against the C oracle across 54 seed and
/// depth combinations. These pin the placement rules behind those levels.
/// </summary>
public class AllocObjectTests
{
    private const int ScreenHeight = 22;
    private const int ScreenWidth = 66;

    /// <summary>Builds a complete level, monsters excepted.</summary>
    private static (GameState Game, DungeonGenerator Generator) PopulatedLevel(uint seed, int level)
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
        generator.PlaceStreamers();
        generator.PlaceBoundary();
        generator.PlaceJunctionDoors();

        int allocLevel = Math.Clamp(level / 3, 2, 10);
        generator.PlaceStairs(2, game.Rng.RandInt(2) + 2, 3);
        generator.PlaceStairs(1, game.Rng.RandInt(2), 3);

        (int charRow, int charColumn) = generator.NewSpot();
        game.CharacterRow = charRow;
        game.CharacterColumn = charColumn;

        generator.PopulateLevel(allocLevel);

        return (game, generator);
    }

    /// <summary>
    /// Nothing may be scattered onto the player's square. The original explains
    /// why: standing under rubble or on a trap causes trouble.
    /// </summary>
    [Fact]
    public void PopulateLevel_NeverPlacesAnythingUnderThePlayer()
    {
        for (uint seed = 1; seed <= 12; seed++)
        {
            (GameState game, _) = PopulatedLevel(seed, 20);

            Assert.Equal(
                0,
                game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex);
        }
    }

    /// <summary>
    /// Every object index the cave holds must be live, and every live object
    /// must be somewhere - the two views of the list cannot disagree.
    /// </summary>
    [Fact]
    public void PopulateLevel_KeepsTheCaveAndObjectListConsistent()
    {
        (GameState game, _) = PopulatedLevel(12345, 20);
        Cave cave = game.Cave;

        var placed = new HashSet<int>();
        for (int row = 0; row < cave.Height; row++)
        {
            for (int column = 0; column < cave.Width; column++)
            {
                int index = cave[row, column].ObjectIndex;
                if (index == 0)
                {
                    continue;
                }

                Assert.InRange(index, ObjectPool.FirstIndex, game.Objects.Count - 1);
                Assert.True(placed.Add(index), $"object {index} is on two squares");
            }
        }

        Assert.Equal(game.Objects.Count - ObjectPool.FirstIndex, placed.Count);
    }

    /// <summary>
    /// Scattered objects land on walkable ground.
    ///
    /// Doors, stairs and rubble live on blocked or corridor squares by their own
    /// rules. Gold is the interesting exception: place_streamer embeds it inside
    /// magma and quartz veins, so a gold pile in a wall is treasure to tunnel
    /// out rather than a misplacement.
    /// </summary>
    [Fact]
    public void PopulateLevel_ScattersOntoWalkableGround()
    {
        (GameState game, _) = PopulatedLevel(999, 25);
        Cave cave = game.Cave;

        for (int row = 0; row < cave.Height; row++)
        {
            for (int column = 0; column < cave.Width; column++)
            {
                int index = cave[row, column].ObjectIndex;
                if (index == 0)
                {
                    continue;
                }

                byte tval = game.Objects[index].TVal;
                if (tval >= ItemCategory.MinDoors || tval is ItemCategory.UpStair
                    or ItemCategory.DownStair or ItemCategory.Rubble)
                {
                    continue;
                }

                byte feature = cave[row, column].Feature;

                if (tval == ItemCategory.Gold
                    && feature is CaveFeature.MagmaWall or CaveFeature.QuartzWall)
                {
                    continue; // buried in a vein, as place_streamer intends
                }

                Assert.True(
                    CaveSets.IsFloor(feature),
                    $"a {tval} landed on feature {feature}");
            }
        }
    }

    /// <summary>
    /// Rubble is scattered along corridors only, which is what makes it a
    /// nuisance to squeeze past rather than scenery in a room.
    /// </summary>
    [Fact]
    public void PopulateLevel_PutsRubbleInCorridors()
    {
        int rubble = 0;
        for (uint seed = 1; seed <= 10; seed++)
        {
            (GameState game, _) = PopulatedLevel(seed, 30);
            Cave cave = game.Cave;

            for (int row = 0; row < cave.Height; row++)
            {
                for (int column = 0; column < cave.Width; column++)
                {
                    int index = cave[row, column].ObjectIndex;
                    if (index != 0 && game.Objects[index].TVal == ItemCategory.Rubble)
                    {
                        rubble++;
                        Assert.Equal(CaveFeature.BlockedFloor, cave[row, column].Feature);
                    }
                }
            }
        }

        Assert.True(rubble > 0, "no rubble appeared in ten levels");
    }

    /// <summary>
    /// Gold really does end up buried in the mineral veins, which is what makes
    /// tunnelling worth the turns it costs.
    /// </summary>
    [Fact]
    public void PopulateLevel_BuriesSomeGoldInTheVeins()
    {
        int buried = 0;
        for (uint seed = 1; seed <= 10; seed++)
        {
            (GameState game, _) = PopulatedLevel(seed, 25);
            Cave cave = game.Cave;

            for (int row = 0; row < cave.Height; row++)
            {
                for (int column = 0; column < cave.Width; column++)
                {
                    int index = cave[row, column].ObjectIndex;
                    if (index != 0
                        && game.Objects[index].TVal == ItemCategory.Gold
                        && cave[row, column].Feature
                            is CaveFeature.MagmaWall or CaveFeature.QuartzWall)
                    {
                        buried++;
                    }
                }
            }
        }

        Assert.True(buried > 0, "no gold was buried in any vein across ten levels");
    }

    [Fact]
    public void PopulateLevel_ProducesGoldTreasureAndTraps()
    {
        var categories = new HashSet<byte>();
        for (uint seed = 1; seed <= 10; seed++)
        {
            (GameState game, _) = PopulatedLevel(seed, 25);
            for (int i = ObjectPool.FirstIndex; i < game.Objects.Count; i++)
            {
                categories.Add(game.Objects[i].TVal);
            }
        }

        Assert.Contains(ItemCategory.Gold, categories);
        Assert.Contains(ItemCategory.InvisibleTrap, categories);
        Assert.Contains(ItemCategory.Rubble, categories);
    }

    /// <summary>
    /// Three of cave_gen's counts come from randnor and can land below zero. A
    /// negative count must simply place nothing rather than throwing or looping.
    /// </summary>
    [Fact]
    public void AllocObject_WithANegativeCount_PlacesNothing()
    {
        (GameState game, DungeonGenerator generator) = PopulatedLevel(4242, 20);
        int before = game.Objects.Count;

        generator.AllocObject(CaveSets.IsFloor, DungeonGenerator.Scatter.Gold, -5);

        Assert.Equal(before, game.Objects.Count);
    }

    [Fact]
    public void PopulateLevel_IsReproducibleForASeed()
    {
        (GameState first, _) = PopulatedLevel(777, 20);
        (GameState second, _) = PopulatedLevel(777, 20);

        Assert.Equal(first.Rng.State, second.Rng.State);
        Assert.Equal(first.Objects.Count, second.Objects.Count);
        Assert.Equal(first.CharacterRow, second.CharacterRow);

        for (int i = ObjectPool.FirstIndex; i < first.Objects.Count; i++)
        {
            Assert.Equal(first.Objects[i].Index, second.Objects[i].Index);
            Assert.Equal(first.Objects[i].Cost, second.Objects[i].Cost);
        }
    }
}
