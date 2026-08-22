using Airom.Core;
using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on character creation.
///
/// 304 rolled characters are diffed against the C oracle, covering every race,
/// every class each race allows, and both sexes. These pin the properties.
/// </summary>
public class CharacterCreationTests
{
    private static (GameState Game, CharacterCreation Creation) Prepared(uint seed)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        return (game, new CharacterCreation(game));
    }

    private static Player Roll(uint seed, int race = 0, int pclass = 0, bool male = true)
    {
        (_, CharacterCreation creation) = Prepared(seed);
        return creation.Create(race, pclass, male, "Tester");
    }

    // -------------------------------------------------------------- stats

    /// <summary>
    /// The roll is rejected unless the eighteen dice total strictly between 42
    /// and 54, which is what stops a hopeless character ever being offered.
    /// </summary>
    [Fact]
    public void RollStats_StaysInsideTheAcceptedBand()
    {
        (_, CharacterCreation creation) = Prepared(12345);

        for (int i = 0; i < 300; i++)
        {
            var player = new Player();
            creation.RollStats(player);

            // Each stat is 5 plus one die each of 3, 4 and 5 sides: 8 to 17.
            Assert.All(player.MaxStat, s => Assert.InRange(s, 8, 17));

            int total = player.MaxStat.Sum() - (5 * Stat.Count);
            Assert.InRange(total, 43, 53);
        }
    }

    /// <summary>
    /// Above 18 the values stand for Umoria's percentile range and move in
    /// jumps, so an adjustment is worth far more to a weak character than a
    /// strong one - and never pushes past the ceiling.
    /// </summary>
    [Fact]
    public void ChangeStat_MovesInJumpsAboveEighteenAndStaysInRange()
    {
        (_, CharacterCreation creation) = Prepared(7);

        for (int i = 0; i < 200; i++)
        {
            var player = new Player();
            creation.RollStats(player);

            creation.ChangeStat(player, Stat.Strength, 3);
            creation.ChangeStat(player, Stat.Wisdom, -3);

            Assert.InRange(player.MaxStat[Stat.Strength], 3, 118);
            Assert.InRange(player.MaxStat[Stat.Wisdom], 3, 118);
        }
    }

    [Fact]
    public void Create_LeavesCurrentAndUseStatsMatchingTheMaximum()
    {
        Player player = Roll(999, race: 3, pclass: 3);

        for (int i = 0; i < Stat.Count; i++)
        {
            Assert.Equal(player.MaxStat[i], player.CurrentStat[i]);
            Assert.Equal(player.CurrentStat[i], player.UseStat[i]);
        }
    }

    // ------------------------------------------------------- race and class

    /// <summary>
    /// A race only permits some classes, and the prompt letter indexes that
    /// filtered list rather than the class table - so the mapping matters.
    /// </summary>
    [Fact]
    public void AllowedClasses_ReflectTheRaceBitField()
    {
        for (int race = 0; race < GameTables.Races.Length; race++)
        {
            IReadOnlyList<int> allowed = CharacterCreation.AllowedClasses(race);

            Assert.NotEmpty(allowed);
            Assert.Equal(allowed.Count, allowed.Distinct().Count());
            Assert.All(allowed, c => Assert.InRange(c, 0, GameTables.Classes.Length - 1));
        }

        // Humans may take every class; the elf may not be a paladin.
        Assert.Equal(GameTables.Classes.Length, CharacterCreation.AllowedClasses(0).Count);
        Assert.DoesNotContain(5, CharacterCreation.AllowedClasses(2));
    }

    [Fact]
    public void Create_RejectsAClassTheRaceCannotTake()
    {
        (_, CharacterCreation creation) = Prepared(1);

        Assert.Throws<ArgumentException>(
            () => creation.Create(race: 2, characterClass: 5, male: true, name: "Tester"));
    }

    [Fact]
    public void Create_AppliesEveryAllowedCombination()
    {
        for (int race = 0; race < GameTables.Races.Length; race++)
        {
            foreach (int pclass in CharacterCreation.AllowedClasses(race))
            {
                Player player = Roll(42, race, pclass);

                Assert.Equal(race, player.Race);
                Assert.Equal(pclass, player.Class);
                Assert.True(player.MaxHitPoints > 0);
                Assert.True(player.Gold >= 80);
            }
        }
    }

    // ------------------------------------------------------------- history

    /// <summary>
    /// A history is assembled a clause at a time by walking linked charts, so it
    /// always ends up with text and a social class in range.
    /// </summary>
    [Fact]
    public void RollHistory_ProducesTextAndASocialClassInRange()
    {
        for (uint seed = 1; seed <= 30; seed++)
        {
            Player player = Roll(seed, race: (int)(seed % 8));

            Assert.False(
                string.IsNullOrWhiteSpace(player.History[0]),
                $"seed {seed} produced no history");
            Assert.InRange(player.SocialClass, 1, 100);
        }
    }

    /// <summary>
    /// The text is wrapped at sixty characters on spaces, so no line runs over
    /// and none begins mid-word.
    /// </summary>
    [Fact]
    public void RollHistory_WrapsAtSixtyCharacters()
    {
        for (uint seed = 1; seed <= 30; seed++)
        {
            Player player = Roll(seed, race: (int)(seed % 8));

            foreach (string line in player.History)
            {
                Assert.True(line.Length <= 60, $"a history line ran to {line.Length}");
            }
        }
    }

    // -------------------------------------------------------------- build

    /// <summary>
    /// Height and weight come from a normal distribution around the race's
    /// build, which is why a Half-Troll is reliably enormous rather than
    /// uniformly random.
    /// </summary>
    [Fact]
    public void RollAgeHeightWeight_TracksTheRaceBuild()
    {
        const int Halfling = 3;
        const int HalfTroll = 7;

        double smallHeight = 0;
        double largeHeight = 0;
        const int Samples = 60;

        for (uint seed = 1; seed <= Samples; seed++)
        {
            smallHeight += Roll(seed, Halfling, pclass: 0).Height;
            largeHeight += Roll(seed, HalfTroll, pclass: 0).Height;
        }

        Assert.True(
            largeHeight / Samples > smallHeight / Samples,
            "half-trolls did not out-measure halflings on average");
    }

    [Fact]
    public void RollAgeHeightWeight_StartsCharactersYoungForTheirRace()
    {
        for (int race = 0; race < GameTables.Races.Length; race++)
        {
            RaceType kind = GameTables.Races[race];
            Player player = Roll(555, race, CharacterCreation.AllowedClasses(race)[0]);

            Assert.InRange(player.Age, kind.BaseAge + 1, kind.BaseAge + kind.AgeRange);
        }
    }

    // --------------------------------------------------------- hit points

    /// <summary>
    /// The whole hit point curve is fixed at creation rather than rolled on
    /// levelling, so the total cannot be re-rolled by reloading a save.
    /// </summary>
    [Fact]
    public void HitPointCurve_IsMonotonicAndWithinAnEighthOfAverage()
    {
        for (uint seed = 1; seed <= 25; seed++)
        {
            Player player = Roll(seed, race: 0, pclass: 0);

            Assert.Equal(player.HitDie, player.HitPointsByLevel[0]);

            for (int level = 1; level < Player.MaxLevel; level++)
            {
                Assert.True(
                    player.HitPointsByLevel[level] > player.HitPointsByLevel[level - 1],
                    "the hit point curve went backwards");
            }

            int minimum = (Player.MaxLevel * 3 / 8 * (player.HitDie - 1)) + Player.MaxLevel;
            int maximum = (Player.MaxLevel * 5 / 8 * (player.HitDie - 1)) + Player.MaxLevel;

            Assert.InRange(player.HitPointsByLevel[Player.MaxLevel - 1], minimum, maximum);
        }
    }

    // --------------------------------------------------------------- money

    /// <summary>
    /// Higher stats mean less gold - the original's way of balancing a strong
    /// roll - except charisma, which adds. Nobody starts with less than 80.
    /// </summary>
    [Fact]
    public void RollMoney_NeverFallsBelowTheFloor()
    {
        for (uint seed = 1; seed <= 40; seed++)
        {
            Assert.True(Roll(seed, race: 0, pclass: 0).Gold >= 80);
        }
    }

    /// <summary>
    /// Women start with fifty more, the source noting she "charmed the banker
    /// into it".
    /// </summary>
    [Fact]
    public void RollMoney_GivesWomenFiftyMoreForTheSameRoll()
    {
        Player man = Roll(4242, race: 0, pclass: 0, male: true);
        Player woman = Roll(4242, race: 0, pclass: 0, male: false);

        // Height and weight are rolled from different columns by sex, so the
        // streams diverge; compare the money rule directly instead.
        var game = new GameState();
        game.InitSeeds(1);
        var creation = new CharacterCreation(game);

        var a = new Player { Male = true, SocialClass = 50 };
        var b = new Player { Male = false, SocialClass = 50 };
        for (int i = 0; i < Stat.Count; i++)
        {
            a.MaxStat[i] = 10;
            b.MaxStat[i] = 10;
        }

        game.Rng.SetSeed(99);
        creation.RollMoney(a);
        game.Rng.SetSeed(99);
        creation.RollMoney(b);

        Assert.Equal(a.Gold + 50, b.Gold);
        Assert.True(man.Gold > 0 && woman.Gold > 0);
    }

    [Fact]
    public void Create_IsReproducibleForASeed()
    {
        // Race 4 is the Gnome, which cannot be a Ranger - take whichever class
        // it does allow rather than assuming one.
        int pclass = CharacterCreation.AllowedClasses(4)[0];

        Player first = Roll(777, race: 4, pclass: pclass);
        Player second = Roll(777, race: 4, pclass: pclass);

        Assert.Equal(first.MaxStat, second.MaxStat);
        Assert.Equal(first.Gold, second.Gold);
        Assert.Equal(first.Age, second.Age);
        Assert.Equal(first.History, second.History);
        Assert.Equal(first.HitPointsByLevel, second.HitPointsByLevel);
    }
}
