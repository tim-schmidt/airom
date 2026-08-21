using Airom.Oracle;

namespace Airom.Tests;

/// <summary>
/// Pins the reference dump format.
///
/// The whole value of the oracle is that AIrom and the 1989 C print byte-
/// identical text, so a diff points at a real divergence. If this side's layout
/// drifts - a renamed key, a reordered line, a stray space - every comparison
/// fails for a reason that has nothing to do with the game. These tests make
/// that drift a test failure rather than a confusing diff.
///
/// The counterpart is ORACLE_FORMAT and the printf calls in
/// tools/oracle/oracle_main.c; the two move together.
/// </summary>
[Collection("game files")]
public class OracleDumpTests
{
    private static string Dump(uint seed, long count)
    {
        var writer = new StringWriter();
        OracleDump.DumpRng(writer, seed, count);
        return writer.ToString();
    }

    private static string[] Lines(string dump) =>
        dump.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Dump_StartsWithTheVersionedHeader()
    {
        string[] lines = Lines(Dump(1, 3));

        Assert.Equal("# airom-oracle 1", lines[0]);
        Assert.Equal("mode rng", lines[1]);
        Assert.Equal("seed 1", lines[2]);
        Assert.Equal("count 3", lines[3]);
    }

    /// <summary>
    /// Line endings must be bare LF on every platform. The C side prints "\n"
    /// through stdio, so emitting CRLF here would make every line differ.
    /// </summary>
    [Fact]
    public void Dump_UsesUnixLineEndings()
    {
        string dump = Dump(1, 3);

        Assert.DoesNotContain('\r', dump);
        Assert.EndsWith("\n", dump, StringComparison.Ordinal);
    }

    /// <summary>
    /// set_rnd_seed() folds its argument to 1..M-1, so a seed of 1 starts the
    /// generator at state 2 rather than 1. Recording the state before any draw
    /// separates a seeding bug from a generator bug in the diff.
    /// </summary>
    [Fact]
    public void Dump_RecordsTheStateBeforeAnyDraw()
    {
        Assert.Contains("state-after-set-seed 2", Dump(1, 1), StringComparison.Ordinal);
        Assert.Contains("state-after-set-seed 1", Dump(0, 1), StringComparison.Ordinal);
    }

    /// <summary>
    /// Values are numbered from one and appear in order, so a diff reports which
    /// draw first diverged rather than only that something did.
    /// </summary>
    [Fact]
    public void Dump_NumbersValuesFromOneInOrder()
    {
        string[] lines = Lines(Dump(7, 5));
        string[] values = [.. lines.Where(l => l.StartsWith("value ", StringComparison.Ordinal))];

        Assert.Equal(5, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            Assert.StartsWith($"value {i + 1} ", values[i], StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The last value drawn is the generator's state, since rnd() returns the
    /// state it just computed. A final-state that disagrees would mean the dump
    /// is reporting something other than what it drew.
    /// </summary>
    [Fact]
    public void Dump_FinalStateEqualsTheLastValue()
    {
        string[] lines = Lines(Dump(12345, 50));

        string lastValue = lines.Last(l => l.StartsWith("value ", StringComparison.Ordinal));
        string finalState = lines.Single(l => l.StartsWith("final-state ", StringComparison.Ordinal));

        Assert.Equal(lastValue.Split(' ')[2], finalState.Split(' ')[1]);
    }

    /// <summary>
    /// A run of zero draws still has to produce a well-formed dump, or the
    /// degenerate case diffs as a format error.
    /// </summary>
    [Fact]
    public void Dump_WithNoValues_IsStillWellFormed()
    {
        string[] lines = Lines(Dump(99, 0));

        Assert.DoesNotContain(lines, l => l.StartsWith("value ", StringComparison.Ordinal));
        Assert.Contains("final-state 100", lines);
        Assert.Contains("state-after-set-seed 100", lines);
    }

    /// <summary>
    /// Known values, computed from Park-Miller independently of the port. Seed 1
    /// folds to state 2, and the first draw is 16807 * 2.
    /// </summary>
    [Fact]
    public void Dump_ProducesTheExpectedSequence()
    {
        string[] lines = Lines(Dump(1, 3));

        Assert.Contains("value 1 33614", lines);
        Assert.Contains("value 2 564950498", lines);
        Assert.Contains("value 3 1097816499", lines);
    }

    // ------------------------------------------------------------ dispatch

    [Fact]
    public void Run_RejectsMisuseWithExitCodeTwo()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(2, OracleDump.Run(output, error, []));
        Assert.Equal(2, OracleDump.Run(output, error, ["nonsense"]));
        Assert.Equal(2, OracleDump.Run(output, error, ["rng", "1"]));
        Assert.Equal(2, OracleDump.Run(output, error, ["rng", "notanumber", "5"]));
        Assert.Equal(string.Empty, output.ToString());
    }

    /// <summary>
    /// Every mode now has a working implementation on this side. Exit code 3
    /// once meant "that part of the port does not exist yet"; nothing reports it
    /// any more, and this asserts that rather than leaving the claim implicit.
    /// </summary>
    [Theory]
    [InlineData("rng", "1", "5")]
    [InlineData("seeds", "1")]
    [InlineData("streamers", "1", "5")]
    [InlineData("rooms", "1", "5", "0")]
    [InlineData("tunnels", "1", "5")]
    [InlineData("stairs", "1", "5")]
    [InlineData("picks", "1", "5", "3")]
    [InlineData("enchanted", "1", "5", "3")]
    [InlineData("populate", "1", "5")]
    [InlineData("cave", "1", "5")]
    public void Run_EveryModeProducesADump(string mode, params string[] rest)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int code = OracleDump.Run(output, error, [mode, .. rest]);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains($"mode {mode}", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("final-state ", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_SeedsProducesADump()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(0, OracleDump.Run(output, error, ["seeds", "12345"]));
        Assert.Contains("mode seeds", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Run_RngMatchesTheDirectDump()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(0, OracleDump.Run(output, error, ["rng", "42", "10"]));
        Assert.Equal(Dump(42, 10), output.ToString());
    }
}
