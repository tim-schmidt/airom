using Airom.Core;
using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on the monster index, the depth-weighted draw, and placement.
///
/// Whole populated levels including their monsters are diffed against the C
/// oracle across 63 seed and depth combinations. These pin the properties
/// behind those levels.
/// </summary>
public class MonsterPlacementTests
{
    private static (GameState Game, DungeonGenerator Generator) Prepared(uint seed, int level)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;
        game.Objects.Reset();
        game.Monsters.Reset();
        game.Cave.Resize(66, 198);
        game.Cave.Blank();

        // A bare room to place into, walled so nothing escapes the edges.
        var generator = new DungeonGenerator(game);
        for (int row = 1; row < 65; row++)
        {
            for (int column = 1; column < 197; column++)
            {
                game.Cave[row, column].Feature = CaveFeature.DarkFloor;
            }
        }

        generator.PlaceBoundary();
        game.CharacterRow = 33;
        game.CharacterColumn = 99;

        return (game, generator);
    }

    // ------------------------------------------------------------- the index

    /// <summary>
    /// The last two creatures win the game and are placed deliberately, never
    /// drawn, so the index deliberately stops short of them.
    /// </summary>
    [Fact]
    public void MonsterLevels_ExcludeTheWinMonsters()
    {
        int indexed = MonsterLevels.LevelTotals[MonsterLevels.MaxMonsterLevel];

        Assert.Equal(
            GameTables.CreatureList.Length - MonsterLevels.WinMonsterCount,
            indexed);
        Assert.Equal(2, MonsterLevels.WinMonsterCount);
    }

    [Fact]
    public void MonsterLevels_TotalsAreNonDecreasing()
    {
        for (int level = 1; level <= MonsterLevels.MaxMonsterLevel; level++)
        {
            Assert.True(
                MonsterLevels.LevelTotals[level] >= MonsterLevels.LevelTotals[level - 1],
                $"totals fell between level {level - 1} and {level}");
        }
    }

    // -------------------------------------------------------------- the draw

    [Fact]
    public void GetMonsterNumber_ReturnsADrawableCreature()
    {
        (_, DungeonGenerator generator) = Prepared(12345, 20);

        for (int i = 0; i < 2000; i++)
        {
            int index = generator.GetMonsterNumber(20);
            Assert.InRange(
                index,
                0,
                GameTables.CreatureList.Length - MonsterLevels.WinMonsterCount - 1);
        }
    }

    /// <summary>
    /// The win monsters must never come out of the ordinary draw - the Balrog
    /// turning up on level 3 would end the game rather early.
    /// </summary>
    [Fact]
    public void GetMonsterNumber_NeverDrawsAWinMonster()
    {
        for (uint seed = 1; seed <= 8; seed++)
        {
            (_, DungeonGenerator generator) = Prepared(seed, 40);

            for (int i = 0; i < 1000; i++)
            {
                CreatureType creature =
                    GameTables.CreatureList[generator.GetMonsterNumber(40)];
                Assert.False(
                    creature.WinsGameWhenKilled,
                    $"seed {seed}: the draw produced {creature.Name}");
            }
        }
    }

    [Fact]
    public void GetMonsterNumber_DrawsDeeperMonstersAtDepth()
    {
        double shallow = AverageLevel(3);
        double deep = AverageLevel(35);

        Assert.True(deep > shallow, $"depth 35 averaged {deep}, depth 3 averaged {shallow}");
    }

    private static double AverageLevel(int level)
    {
        (_, DungeonGenerator generator) = Prepared(999, level);

        long total = 0;
        const int Samples = 2000;
        for (int i = 0; i < Samples; i++)
        {
            total += GameTables.CreatureList[generator.GetMonsterNumber(level)].Level;
        }

        return (double)total / Samples;
    }

    /// <summary>
    /// One draw in fifty reaches deeper than the level allows, which is where
    /// the occasional monster far out of its depth comes from.
    /// </summary>
    [Fact]
    public void GetMonsterNumber_SometimesReachesDeeperThanTheLevel()
    {
        (_, DungeonGenerator generator) = Prepared(4242, 10);

        int outOfDepth = 0;
        for (int i = 0; i < 5000; i++)
        {
            if (GameTables.CreatureList[generator.GetMonsterNumber(10)].Level > 10)
            {
                outOfDepth++;
            }
        }

        Assert.True(outOfDepth > 0, "no out-of-depth monster appeared in 5000 draws");
    }

    // ----------------------------------------------------------- placement

    [Fact]
    public void PlaceMonster_FillsInTheMonsterAndTheSquare()
    {
        (GameState game, DungeonGenerator generator) = Prepared(777, 20);

        Assert.True(generator.PlaceMonster(10, 10, 50, asleep: true));

        int index = game.Cave[10, 10].MonsterIndex;
        Assert.Equal(MonsterPool.FirstIndex, index);

        Monster monster = game.Monsters[index];
        Assert.Equal(10, monster.Row);
        Assert.Equal(10, monster.Column);
        Assert.Equal(50, monster.CreatureIndex);
        Assert.True(monster.HitPoints > 0);
        Assert.False(monster.Visible);
    }

    /// <summary>
    /// The creature table stores speed offset by ten so it fits a byte. A
    /// monster that came out at 11 rather than 1 would move ten times too often.
    /// </summary>
    [Fact]
    public void PlaceMonster_UnpacksTheSpeedOffset()
    {
        (GameState game, DungeonGenerator generator) = Prepared(1, 20);

        generator.PlaceMonster(10, 10, 0, asleep: false); // Filthy Street Urchin, speed 11

        Assert.Equal(11, GameTables.CreatureList[0].Speed);
        Assert.Equal(1, game.Monsters[MonsterPool.FirstIndex].Speed);
    }

    /// <summary>
    /// Creatures flagged for maximum hit points roll none - they always arrive
    /// at the top of their range.
    /// </summary>
    [Fact]
    public void PlaceMonster_GivesMaximumHitPointsWhereFlagged()
    {
        int index = Array.FindIndex(
            GameTables.CreatureList,
            c => c.HasDefense(CreatureDefense.MaxHitPoints));
        Assert.True(index >= 0, "no creature carries the max hit points flag");

        (GameState game, DungeonGenerator generator) = Prepared(5, 20);
        generator.PlaceMonster(10, 10, index, asleep: false);

        CreatureType kind = GameTables.CreatureList[index];
        Assert.Equal(
            kind.HitDiceCount * kind.HitDiceSides,
            game.Monsters[MonsterPool.FirstIndex].HitPoints);
    }

    /// <summary>
    /// Asking for a sleeping monster is not enough: a creature with no sleep
    /// value in the table is permanently alert and wakes immediately.
    /// </summary>
    [Fact]
    public void PlaceMonster_LeavesAlwaysAlertCreaturesAwake()
    {
        int index = Array.FindIndex(GameTables.CreatureList, c => c.Sleep == 0);
        Assert.True(index >= 0);

        (GameState game, DungeonGenerator generator) = Prepared(9, 20);
        generator.PlaceMonster(10, 10, index, asleep: true);

        Assert.Equal(0, game.Monsters[MonsterPool.FirstIndex].Sleep);
    }

    [Fact]
    public void MonsterPool_StartsAtTwoBecauseOneIsThePlayer()
    {
        var pool = new MonsterPool();

        Assert.Equal(2, MonsterPool.FirstIndex);
        Assert.Equal(MonsterPool.FirstIndex, pool.Count);
    }

    // ---------------------------------------------------------- scattering

    /// <summary>
    /// A minimum distance keeps new arrivals off the player, which is what stops
    /// a fresh level opening with something already adjacent.
    /// </summary>
    [Fact]
    public void AllocMonster_RespectsTheMinimumDistance()
    {
        (GameState game, DungeonGenerator generator) = Prepared(12345, 20);

        generator.AllocMonster(40, minimumDistance: 10, asleep: true);

        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            Monster monster = game.Monsters[i];
            int distance = Cave.Distance(
                monster.Row, monster.Column, game.CharacterRow, game.CharacterColumn);
            Assert.True(distance > 10, $"a monster started {distance} away");
        }
    }

    [Fact]
    public void AllocMonster_NeverStacksTwoOnASquare()
    {
        (GameState game, DungeonGenerator generator) = Prepared(999, 25);

        generator.AllocMonster(60, minimumDistance: 0, asleep: true);

        var seen = new HashSet<(int, int)>();
        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            Monster monster = game.Monsters[i];
            Assert.True(
                seen.Add((monster.Row, monster.Column)),
                $"two monsters at ({monster.Row},{monster.Column})");
            Assert.Equal(i, game.Cave[monster.Row, monster.Column].MonsterIndex);
        }
    }

    /// <summary>
    /// Dragons always start asleep whatever the caller asked, which the original
    /// notes is to give the player a sporting chance.
    /// </summary>
    [Fact]
    public void AllocMonster_AlwaysPutsDragonsToSleep()
    {
        int dragons = 0;
        for (uint seed = 1; seed <= 20; seed++)
        {
            (GameState game, DungeonGenerator generator) = Prepared(seed, 40);
            generator.AllocMonster(60, minimumDistance: 0, asleep: false);

            for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
            {
                Monster monster = game.Monsters[i];
                CreatureType kind = GameTables.CreatureList[monster.CreatureIndex];

                if (kind.DisplayChar is not ('d' or 'D'))
                {
                    continue;
                }

                dragons++;

                // Unless the creature never sleeps at all, it must be asleep.
                if (kind.Sleep != 0)
                {
                    Assert.True(monster.Sleep > 0, $"{kind.Name} was placed awake");
                }
            }
        }

        Assert.True(dragons > 0, "no dragons appeared in twenty levels");
    }

    [Fact]
    public void AllocMonster_IsReproducibleForASeed()
    {
        (GameState first, DungeonGenerator firstGenerator) = Prepared(555, 30);
        firstGenerator.AllocMonster(30, 0, true);

        (GameState second, DungeonGenerator secondGenerator) = Prepared(555, 30);
        secondGenerator.AllocMonster(30, 0, true);

        Assert.Equal(first.Monsters.Count, second.Monsters.Count);
        for (int i = MonsterPool.FirstIndex; i < first.Monsters.Count; i++)
        {
            Assert.Equal(first.Monsters[i].CreatureIndex, second.Monsters[i].CreatureIndex);
            Assert.Equal(first.Monsters[i].HitPoints, second.Monsters[i].HitPoints);
            Assert.Equal(first.Monsters[i].Row, second.Monsters[i].Row);
        }
    }

    // ------------------------------------------------------------- distance

    /// <summary>
    /// Umoria's distance is not Euclidean: the larger axis plus half the
    /// smaller, which approximates a circle closely enough without square roots.
    /// </summary>
    [Fact]
    public void Distance_MatchesTheOriginalApproximation()
    {
        Assert.Equal(0, Cave.Distance(5, 5, 5, 5));
        Assert.Equal(3, Cave.Distance(0, 0, 0, 3));
        Assert.Equal(3, Cave.Distance(0, 0, 3, 0));
        Assert.Equal(4, Cave.Distance(0, 0, 3, 3)); // 3 + 3/2
        Assert.Equal(Cave.Distance(0, 0, 7, 2), Cave.Distance(7, 2, 0, 0));
    }
}
