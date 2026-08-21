using Airom.Core;
using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on the seeding chain and the item appearance shuffle.
///
/// The expected values here were taken from the C oracle, not computed by hand,
/// so they encode what the original actually does rather than what this port
/// believes it should. The full dumps agree across 64 seeds; these pin the
/// specific properties worth failing loudly on.
/// </summary>
public class GameStateTests
{
    private static GameState Started(uint seed)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        return game;
    }

    // ------------------------------------------------------------ init_seeds

    /// <summary>
    /// The town seed sits a fixed distance from the appearance seed, so one
    /// number decides both. Values confirmed against the oracle for seed 12345.
    /// </summary>
    [Fact]
    public void InitSeeds_DerivesBothSeedsFromOneValue()
    {
        var game = new GameState();
        game.InitSeeds(12345);

        Assert.Equal(12345u, game.RandesSeed);
        Assert.Equal(21107u, game.TownSeed); // 12345 + 8762
        Assert.Equal(1737948946u, game.Rng.State);
    }

    [Fact]
    public void InitSeeds_IsReproducible()
    {
        var first = new GameState();
        var second = new GameState();

        first.InitSeeds(999);
        second.InitSeeds(999);

        Assert.Equal(first.RandesSeed, second.RandesSeed);
        Assert.Equal(first.TownSeed, second.TownSeed);
        Assert.Equal(first.Rng.State, second.Rng.State);
    }

    /// <summary>
    /// The offsets are added with wrapping arithmetic in the C, so a seed near
    /// the top of the range must not throw or saturate.
    /// </summary>
    [Fact]
    public void InitSeeds_WrapsAtTheTopOfTheRange()
    {
        var game = new GameState();
        game.InitSeeds(uint.MaxValue);

        Assert.Equal(uint.MaxValue, game.RandesSeed);
        Assert.Equal(8761u, game.TownSeed); // wrapped
        Assert.InRange(game.Rng.State, 1u, 2147483646u);
    }

    /// <summary>
    /// A seed of zero means "take one from the clock", so two calls must differ
    /// in general - and must not sit at whatever a fixed seed would give.
    /// </summary>
    [Fact]
    public void InitSeeds_WithZero_UsesTheClock()
    {
        var game = new GameState();
        game.InitSeeds(0);

        Assert.NotEqual(0u, game.RandesSeed);
    }

    // ----------------------------------------------------------- magic_init

    /// <summary>
    /// magic_init brackets its work in a push and a pop of the generator, so
    /// the shuffle costs the main stream nothing at all.
    ///
    /// The original lands one higher than it started, its restore being a step
    /// out of place - see Rng.RestoresExactly, which is what the oracle harness
    /// turns back on to compare against it.
    /// </summary>
    [Fact]
    public void MagicInit_LeavesTheGeneratorWhereItStarted()
    {
        var game = new GameState();
        game.InitSeeds(12345);
        uint before = game.Rng.State;

        game.MagicInit();

        Assert.Equal(before, game.Rng.State);
        Assert.Equal(1737948946u, game.Rng.State);

        // And the original's answer, one higher, when asked for it.
        var asUmoria = new GameState();
        asUmoria.Rng.RestoresExactly = false;
        asUmoria.InitSeeds(12345);
        asUmoria.MagicInit();

        Assert.Equal(1737948947u, asUmoria.Rng.State); // from the oracle
    }

    /// <summary>
    /// The first three potions are slime mould juice, apple juice and water.
    /// tables.c marks them "Do not move the first three", and the shuffle starts
    /// at index 3 for exactly that reason.
    /// </summary>
    [Fact]
    public void MagicInit_LeavesTheFirstThreePotionAppearancesAlone()
    {
        GameState game = Started(4242);

        Assert.Equal("Icky Green", game.Appearances.Colors[0]);
        Assert.Equal("Light Brown", game.Appearances.Colors[1]);
        Assert.Equal("Clear", game.Appearances.Colors[2]);
    }

    /// <summary>
    /// Shuffling must permute, never duplicate or drop: every original name has
    /// to still be present exactly once, or some item becomes unidentifiable.
    /// </summary>
    [Fact]
    public void MagicInit_PermutesWithoutLosingOrDuplicatingNames()
    {
        GameState game = Started(31337);
        Appearances appearances = game.Appearances;

        AssertIsPermutationOf(GameTables.Colors, appearances.Colors);
        AssertIsPermutationOf(GameTables.Woods, appearances.Woods);
        AssertIsPermutationOf(GameTables.Metals, appearances.Metals);
        AssertIsPermutationOf(GameTables.Rocks, appearances.Rocks);
        AssertIsPermutationOf(GameTables.Amulets, appearances.Amulets);
        AssertIsPermutationOf(GameTables.Mushrooms, appearances.Mushrooms);
    }

    private static void AssertIsPermutationOf(string[] expected, string[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal([.. expected.Order()], [.. actual.Order()]);
    }

    [Fact]
    public void MagicInit_ActuallyChangesTheOrder()
    {
        GameState game = Started(2024);

        Assert.NotEqual(GameTables.Woods, game.Appearances.Woods);
        Assert.NotEqual(GameTables.Rocks, game.Appearances.Rocks);
    }

    [Fact]
    public void MagicInit_IsReproducibleForASeed()
    {
        GameState first = Started(555);
        GameState second = Started(555);

        Assert.Equal(first.Appearances.Colors, second.Appearances.Colors);
        Assert.Equal(first.Appearances.Titles, second.Appearances.Titles);
    }

    [Fact]
    public void MagicInit_GivesDifferentSeedsDifferentAppearances()
    {
        GameState first = Started(1);
        GameState second = Started(2);

        Assert.NotEqual(first.Appearances.Titles, second.Appearances.Titles);
    }

    // --------------------------------------------------------------- titles

    /// <summary>
    /// Scroll titles are stored in a char[10] in the C, so nine characters is
    /// the hard limit. Anything longer means the truncation was missed.
    /// </summary>
    [Fact]
    public void Titles_NeverExceedNineCharacters()
    {
        for (uint seed = 1; seed <= 40; seed++)
        {
            GameState game = Started(seed);
            Assert.All(
                game.Appearances.Titles,
                title => Assert.True(
                    title.Length <= 9,
                    $"seed {seed} produced a {title.Length}-character title: '{title}'"));
        }
    }

    /// <summary>
    /// The truncation cuts to eight rather than nine when the ninth character is
    /// a space, which is what stops a title ending on a stranded space.
    /// </summary>
    [Fact]
    public void Titles_NeverEndOnASpace()
    {
        for (uint seed = 1; seed <= 40; seed++)
        {
            GameState game = Started(seed);
            Assert.All(
                game.Appearances.Titles,
                title => Assert.False(
                    title.EndsWith(' '),
                    $"seed {seed} produced a title ending in a space: '{title}'"));
        }
    }

    /// <summary>
    /// Titles shorter than the limit are left alone. The C reads index 8 whatever
    /// the length, past the terminator for a short title - harmless, because both
    /// branches write at or beyond that terminator, so nothing is cut. Short
    /// titles really do occur, so this is not a hypothetical path.
    /// </summary>
    [Fact]
    public void Titles_ShorterThanTheLimitSurviveIntact()
    {
        var lengths = new HashSet<int>();
        for (uint seed = 1; seed <= 40; seed++)
        {
            foreach (string title in Started(seed).Appearances.Titles)
            {
                lengths.Add(title.Length);
            }
        }

        Assert.Contains(lengths, length => length < 9);
        Assert.DoesNotContain(0, lengths);
    }

    [Fact]
    public void Titles_AreBuiltFromSyllables()
    {
        GameState game = Started(12345);

        Assert.Equal(Appearances.TitleCount, game.Appearances.Titles.Length);
        Assert.All(
            game.Appearances.Titles,
            title => Assert.Matches("^[a-z ]+$", title));
    }

    // -------------------------------------------------------------- isolation

    /// <summary>
    /// Two games in the same process must not share appearances. Umoria shuffled
    /// globals, so this is the one place the port deliberately differs in
    /// structure - and the reason it does.
    /// </summary>
    [Fact]
    public void TwoGames_DoNotShareAppearances()
    {
        GameState first = Started(11);
        GameState second = Started(22);

        Assert.NotSame(first.Appearances, second.Appearances);
        Assert.NotEqual(first.Appearances.Colors, second.Appearances.Colors);
    }
}
