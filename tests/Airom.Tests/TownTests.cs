using Airom.Core;
using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on the town and the game-winning monsters.
///
/// Both are diffed against the C oracle - 63 towns across day and night, and 24
/// levels at depth 50 and beyond. These pin the properties behind them.
/// </summary>
public class TownTests
{
    private static GameState Town(uint seed, int turn)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = 0;
        game.Turn = turn;
        game.Objects.Reset();
        game.Monsters.Reset();
        game.Cave.Resize(DungeonGenerator.TownHeight, DungeonGenerator.TownWidth);
        game.Cave.Blank();

        new DungeonGenerator(game).GenerateTown();
        return game;
    }

    private static string Layout(GameState game)
    {
        var rows = new List<string>();
        for (int row = 0; row < game.Cave.Height; row++)
        {
            var line = new char[game.Cave.Width];
            for (int column = 0; column < game.Cave.Width; column++)
            {
                line[column] = (char)('0' + game.Cave[row, column].Feature);
            }

            rows.Add(new string(line));
        }

        return string.Join('\n', rows);
    }

    [Fact]
    public void Town_IsOneScreen()
    {
        GameState game = Town(12345, 0);

        Assert.Equal(22, game.Cave.Height);
        Assert.Equal(66, game.Cave.Width);
    }

    /// <summary>
    /// Six shops, each with exactly one door, and the doors are distinct so no
    /// two shops share an entrance.
    /// </summary>
    [Fact]
    public void Town_HasSixShopsWithOneDoorEach()
    {
        GameState game = Town(12345, 0);

        var doors = new List<int>();
        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                int index = game.Cave[row, column].ObjectIndex;
                if (index != 0 && game.Objects[index].TVal == ItemCategory.StoreDoor)
                {
                    doors.Add(game.Objects[index].Index);
                }
            }
        }

        Assert.Equal(6, doors.Count);
        Assert.Equal(6, doors.Distinct().Count());
    }

    /// <summary>
    /// The layout is generated from the town seed rather than the running one,
    /// so the town is the same place every time the player climbs out of the
    /// dungeon. The stairs are inside that bracket too, which the original notes
    /// is so they do not move around.
    /// </summary>
    [Fact]
    public void Town_LayoutIsTheSamePlaceEveryVisit()
    {
        string first = Layout(Town(4242, 0));
        string second = Layout(Town(4242, 0));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Town_DifferentSeedsGiveDifferentTowns()
    {
        Assert.NotEqual(Layout(Town(1, 0)), Layout(Town(2, 0)));
    }

    [Fact]
    public void Town_HasExactlyOneDownStaircaseAndNoUpStaircase()
    {
        GameState game = Town(999, 0);

        int down = 0;
        int up = 0;
        for (int i = ObjectPool.FirstIndex; i < game.Objects.Count; i++)
        {
            if (game.Objects[i].TVal == ItemCategory.DownStair)
            {
                down++;
            }
            else if (game.Objects[i].TVal == ItemCategory.UpStair)
            {
                up++;
            }
        }

        Assert.Equal(1, down);
        Assert.Equal(0, up); // there is nowhere above the town to climb to
    }

    /// <summary>
    /// By day the whole map is lit; by night only the buildings are, which is
    /// what makes the open ground dark to walk across.
    /// </summary>
    [Fact]
    public void Town_LightsEverythingByDayAndOnlyBuildingsByNight()
    {
        GameState day = Town(7, 0);
        for (int row = 0; row < day.Cave.Height; row++)
        {
            for (int column = 0; column < day.Cave.Width; column++)
            {
                Assert.True(day.Cave[row, column].PermanentLight);
            }
        }

        GameState night = Town(7, 5000);
        int darkGround = 0;
        for (int row = 0; row < night.Cave.Height; row++)
        {
            for (int column = 0; column < night.Cave.Width; column++)
            {
                CaveSquare square = night.Cave[row, column];
                if (square.Feature == CaveFeature.DarkFloor)
                {
                    Assert.False(square.PermanentLight);
                    darkGround++;
                }
                else
                {
                    Assert.True(square.PermanentLight);
                }
            }
        }

        Assert.True(darkGround > 0, "nothing was left dark at night");
    }

    /// <summary>
    /// Twice as many people are about at night, which is what makes the town
    /// worth leaving before dark.
    /// </summary>
    [Fact]
    public void Town_HasMorePeopleAtNight()
    {
        int day = Town(42, 0).Monsters.Count - MonsterPool.FirstIndex;
        int night = Town(42, 5000).Monsters.Count - MonsterPool.FirstIndex;

        Assert.Equal(4, day);
        Assert.Equal(8, night);
    }

    /// <summary>
    /// The clock alternates in blocks of 5000 turns, so the boundary is exact.
    /// </summary>
    [Theory]
    [InlineData(0, 4)]
    [InlineData(4999, 4)]
    [InlineData(5000, 8)]
    [InlineData(9999, 8)]
    [InlineData(10000, 4)]
    public void Town_DayAndNightAlternateEveryFiveThousandTurns(int turn, int expected)
    {
        Assert.Equal(expected, Town(3, turn).Monsters.Count - MonsterPool.FirstIndex);
    }

    /// <summary>
    /// Shop walls are the same indestructible rock as the edge of the map, so
    /// the only way in is the door.
    /// </summary>
    [Fact]
    public void Town_ShopsCannotBeTunnelledInto()
    {
        GameState game = Town(555, 0);

        int walls = 0;
        for (int row = 1; row < game.Cave.Height - 1; row++)
        {
            for (int column = 1; column < game.Cave.Width - 1; column++)
            {
                if (game.Cave[row, column].Feature == CaveFeature.BoundaryWall)
                {
                    walls++;
                }
            }
        }

        Assert.True(walls > 0, "no shop walls were built");
    }

    // ------------------------------------------------------- win monsters

    private static GameState DeepLevel(uint seed, int level)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;
        game.Objects.Reset();
        game.Monsters.Reset();
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();

        new DungeonGenerator(game).CarveCave();
        return game;
    }

    /// <summary>
    /// Win monsters appear from depth 50 and not before, which is what makes
    /// the last stretch of the dungeon different in kind.
    /// </summary>
    [Fact]
    public void WinMonsters_AppearOnlyFromDepthFifty()
    {
        Assert.False(HasWinMonster(DeepLevel(12345, 49)));
        Assert.True(HasWinMonster(DeepLevel(12345, 50)));
    }

    /// <summary>
    /// Whether one of the two creatures held back for the deepest levels is on
    /// this one. They are the last rows of the table, which is exactly why the
    /// ordinary draw stops short of them - and only one of the two, the Balrog,
    /// actually ends the game, so it is the position that says what they are
    /// rather than the flag.
    /// </summary>
    private static bool HasWinMonster(GameState game)
    {
        int first = MonsterLevels.LevelTotals[MonsterLevels.MaxMonsterLevel];

        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            if (game.Monsters[i].CreatureIndex >= first)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A win monster is never placed within sight of the player, and never
    /// asleep - unlike everything else on the level, which arrives sleeping.
    /// </summary>
    [Fact]
    public void WinMonster_StartsAwakeAndOutOfSight()
    {
        for (uint seed = 1; seed <= 8; seed++)
        {
            GameState game = DeepLevel(seed, 60);

            for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
            {
                Monster monster = game.Monsters[i];
                if (!GameTables.CreatureList[monster.CreatureIndex].WinsGameWhenKilled)
                {
                    continue;
                }

                Assert.Equal(0, monster.Sleep);
                Assert.True(
                    Cave.Distance(
                        monster.Row, monster.Column,
                        game.CharacterRow, game.CharacterColumn) > 20,
                    $"seed {seed}: a win monster started within sight");
            }
        }
    }

    [Fact]
    public void WinMonster_IsNotPlacedOnceTheGameIsWon()
    {
        var game = new GameState();
        game.InitSeeds(777);
        game.MagicInit();
        game.DungeonLevel = 60;
        game.TotalWinner = true;
        game.Objects.Reset();
        game.Monsters.Reset();
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();

        new DungeonGenerator(game).CarveCave();

        Assert.False(HasWinMonster(game));
    }
}
