using Airom.Core;

namespace Airom.Tests;

/// <summary>
/// Conformance tests for the ported Park-Miller generator.
///
/// The sequence produced here is not an implementation detail: dungeon
/// generation, monster placement and the town layout all consume it, so a
/// divergence from Umoria's sequence silently changes the game. These tests
/// pin it to the values published with the original algorithm and to the
/// self-check that shipped in Umoria's own rnd.c under #ifdef TEST_RNG.
/// </summary>
public class RngTests
{
    /// <summary>
    /// Park and Miller's published check: starting from z[1] = 1, the value of
    /// z[10001] must be 1043618065. Umoria's rnd.c asserts exactly this.
    /// </summary>
    [Fact]
    public void ParkMillerConformance_Z10001_MatchesPublishedValue()
    {
        var rng = new Rng(0); // set_rnd_seed(0) yields a state of 1, i.e. z[1]

        int value = 0;
        for (int i = 0; i < 10_000; i++)
        {
            value = rng.Next();
        }

        Assert.Equal(1043618065, value);
    }

    [Fact]
    public void SetSeed_MapsZeroToOne()
    {
        var rng = new Rng(0);

        Assert.Equal(1u, rng.State);
    }

    [Fact]
    public void SetSeed_KeepsStateWithinGeneratorRange()
    {
        // Values at and beyond the modulus must fold back into 1 .. M-1.
        foreach (uint seed in new uint[] { 0, 1, 2147483646, 2147483647, uint.MaxValue })
        {
            var rng = new Rng(seed);

            Assert.InRange(rng.State, 1u, 2147483646u);
        }
    }

    [Fact]
    public void Next_StaysWithinOneToModulusMinusOne()
    {
        var rng = new Rng(12345);

        for (int i = 0; i < 100_000; i++)
        {
            Assert.InRange(rng.Next(), 1, 2147483646);
        }
    }

    /// <summary>
    /// The determinism the game actually depends on: a stored seed replays the
    /// same stream. This is what makes the town map and the object appearance
    /// shuffle reproducible across save and reload.
    /// </summary>
    [Fact]
    public void SetSeed_SameValueAlwaysReplaysSameSequence()
    {
        var first = new Rng(9876);
        var second = new Rng(9876);

        int[] expected = Enumerable.Range(0, 100).Select(_ => first.Next()).ToArray();
        int[] replayed = Enumerable.Range(0, 100).Select(_ => second.Next()).ToArray();

        Assert.Equal(expected, replayed);
    }

    [Fact]
    public void PushSeed_SwitchesToTheRequestedReproducibleStream()
    {
        var rng = new Rng(9876);
        for (int i = 0; i < 50; i++)
        {
            rng.Next();
        }

        rng.PushSeed(4242);
        int[] fromPush = Enumerable.Range(0, 20).Select(_ => rng.Next()).ToArray();

        var fresh = new Rng(4242);
        int[] expected = Enumerable.Range(0, 20).Select(_ => fresh.Next()).ToArray();

        Assert.Equal(expected, fromPush);
    }

    /// <summary>
    /// A restored stream carries on exactly where it left off, which is what
    /// the town and the appearance shuffle are bracketed for.
    /// </summary>
    [Fact]
    public void PopSeed_PutsTheStreamBackWhereItWas()
    {
        var rng = new Rng(9876);
        for (int i = 0; i < 50; i++)
        {
            rng.Next();
        }

        uint before = rng.State;
        int[] expected = Enumerable.Range(0, 20).Select(_ => rng.Next()).ToArray();

        var replayed = new Rng(9876);
        for (int i = 0; i < 50; i++)
        {
            replayed.Next();
        }

        replayed.PushSeed(4242);
        replayed.Next();
        replayed.Next();
        replayed.PopSeed();

        Assert.Equal(before, replayed.State);
        Assert.Equal(expected, Enumerable.Range(0, 20).Select(_ => replayed.Next()));
    }

    /// <summary>
    /// The original's restore is not exact: reset_seed() feeds the saved state
    /// back through set_rnd_seed(), which applies (x % (M-1)) + 1 to a value
    /// already in range, so a state of n comes back as n+1 and M-1 comes back
    /// as 1. Because the generator is multiplicative, n and n+1 are unrelated
    /// points on the cycle - the restored stream shares nothing with the one
    /// that was saved, which is plainly not what "reset" was meant to do.
    ///
    /// The port does not copy the bug, but it can, and does for the oracle
    /// harness: the C it is compared against has it and always will.
    /// </summary>
    [Fact]
    public void PopSeed_CanStillSlipTheWayUmoriaDoes()
    {
        var rng = new Rng(9876) { RestoresExactly = false };
        for (int i = 0; i < 50; i++)
        {
            rng.Next();
        }

        uint before = rng.State;
        rng.PushSeed(4242);
        rng.Next();
        rng.PopSeed();

        Assert.Equal(before + 1, rng.State);
    }

    [Fact]
    public void PopSeed_WrapsAtTheTopOfTheRangeWhenItSlips()
    {
        var rng = new Rng(0) { RestoresExactly = false };

        // Drive the state to M-1 = 2147483646, the value that wraps to 1.
        rng.PushSeed(2147483645);
        Assert.Equal(2147483646u, rng.State);

        uint atTop = rng.State;
        rng.PushSeed(1);
        rng.PopSeed();

        Assert.Equal(1u, rng.State);
        Assert.Equal(2147483646u, atTop);
    }

    [Fact]
    public void RandInt_CoversFullInclusiveRange()
    {
        var rng = new Rng(42);
        var seen = new HashSet<int>();

        for (int i = 0; i < 10_000; i++)
        {
            int roll = rng.RandInt(6);
            Assert.InRange(roll, 1, 6);
            seen.Add(roll);
        }

        Assert.Equal(6, seen.Count); // all faces of a d6 appear
    }

    [Fact]
    public void DamRoll_BoundedByDiceCountAndSides()
    {
        var rng = new Rng(7);

        for (int i = 0; i < 10_000; i++)
        {
            Assert.InRange(rng.DamRoll(3, 8), 3, 24);
        }
    }

    [Fact]
    public void DamRoll_WithZeroDice_ReturnsZeroWithoutConsumingRandomness()
    {
        var rng = new Rng(7);
        uint before = rng.State;

        Assert.Equal(0, rng.DamRoll(0, 8));
        Assert.Equal(before, rng.State);
    }

    [Fact]
    public void PDamRoll_MatchesDamRollOnTheSamePair()
    {
        // The monster and item tables store damage as packed {dice, sides} bytes.
        byte[] pair = [2, 6];

        var viaPacked = new Rng(555);
        var viaExplicit = new Rng(555);

        Assert.Equal(viaExplicit.DamRoll(2, 6), viaPacked.PDamRoll(pair));
    }

    /// <summary>
    /// randnor() is the character-generation and monster-speed workhorse. The
    /// table it searches is built for a standard deviation of 64 and is
    /// rescaled, so the distribution is only approximately normal; these bounds
    /// reflect the algorithm's real behaviour rather than a strict Gaussian.
    /// </summary>
    [Fact]
    public void RandNor_CentresOnMeanWithPlausibleSpread()
    {
        var rng = new Rng(2024);
        const int mean = 100;
        const int sd = 20;

        long total = 0;
        int withinOneSd = 0;
        const int samples = 200_000;

        for (int i = 0; i < samples; i++)
        {
            int value = rng.RandNor(mean, sd);
            total += value;

            if (Math.Abs(value - mean) <= sd)
            {
                withinOneSd++;
            }

            // The off-table tail is capped at 5 standard deviations.
            Assert.InRange(value, mean - 5 * sd, mean + 5 * sd);
        }

        double average = (double)total / samples;
        Assert.InRange(average, mean - 1.0, mean + 1.0);

        // Roughly the 68% of a normal distribution, with slack for the
        // table's quantisation and integer rounding.
        double fraction = (double)withinOneSd / samples;
        Assert.InRange(fraction, 0.60, 0.76);
    }
}
