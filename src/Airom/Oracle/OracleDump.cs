// AIrom's half of the reference comparison.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;
using Airom.Data;

namespace Airom.Oracle;

/// <summary>
/// Prints AIrom's state in the same plain-text format as the C oracle in
/// tools/oracle, so the two can be diffed line for line.
///
/// Umoria has no test suite, so "correct" here means "does what the C did".
/// Every dungeon, monster roll and item drop derives from one deterministic
/// generator, which makes an identical dump strong evidence that the whole
/// chain producing it agrees - far broader coverage than any unit test of the
/// same size.
///
/// The format is deliberately dull: line-oriented, ASCII, one fact per line,
/// so a diff points at the first divergence instead of a wall of noise.
/// </summary>
public static class OracleDump
{
    /// <summary>
    /// Format version. Must match ORACLE_FORMAT in tools/oracle/oracle_main.c;
    /// bump both together when the layout changes.
    /// </summary>
    public const int FormatVersion = 1;

    private static void Header(TextWriter output, string mode, uint seed)
    {
        output.Write($"# airom-oracle {FormatVersion}\n");
        output.Write($"mode {mode}\n");
        output.Write(
            "seed " + seed.ToString(CultureInfo.InvariantCulture) + "\n");
    }

    /// <summary>
    /// The generator alone: seed it, then print a run of draws. This is the
    /// narrowest comparison available and the one that has to pass before any
    /// other dump means anything.
    /// </summary>
    public static void DumpRng(TextWriter output, uint seed, long count)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "rng", seed);
        output.Write("count " + count.ToString(CultureInfo.InvariantCulture) + "\n");

        var rng = new Rng(seed);
        output.Write(
            "state-after-set-seed "
            + rng.State.ToString(CultureInfo.InvariantCulture) + "\n");

        for (long i = 1; i <= count; i++)
        {
            int value = rng.Next();
            output.Write(
                "value " + i.ToString(CultureInfo.InvariantCulture)
                + " " + value.ToString(CultureInfo.InvariantCulture) + "\n");
        }

        output.Write(
            "final-state " + rng.State.ToString(CultureInfo.InvariantCulture) + "\n");
    }

    /// <summary>
    /// The seeding chain the game actually uses: init_seeds() derives the
    /// appearance and town seeds and burns a random number of draws, then
    /// magic_init() shuffles appearances inside a push/pop of the generator.
    ///
    /// The two state lines around magic_init are the interesting pair. The
    /// restore is deliberately inexact, so they should differ by exactly one -
    /// which is the quirk measured against the original rather than assumed.
    /// </summary>
    public static void DumpSeeds(TextWriter output, uint seed)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "seeds", seed);

        var game = new GameState();
        game.InitSeeds(seed);

        Line(output, "randes-seed", game.RandesSeed);
        Line(output, "town-seed", game.TownSeed);
        Line(output, "state-after-init-seeds", game.Rng.State);

        game.MagicInit();
        Line(output, "state-after-magic-init", game.Rng.State);

        // The shuffled tables are derived state in their own right, so dumping
        // them catches a divergence inside magic_init rather than only after it.
        Appearances appearances = game.Appearances;
        WriteNames(output, "color", appearances.Colors);
        WriteNames(output, "wood", appearances.Woods);
        WriteNames(output, "metal", appearances.Metals);
        WriteNames(output, "rock", appearances.Rocks);
        WriteNames(output, "amulet", appearances.Amulets);
        WriteNames(output, "mushroom", appearances.Mushrooms);
        WriteNames(output, "title", appearances.Titles);

        Line(output, "final-state", game.Rng.State);
    }

    private static void Line(TextWriter output, string key, uint value) =>
        output.Write(key + " " + value.ToString(CultureInfo.InvariantCulture) + "\n");

    private static void WriteNames(TextWriter output, string key, string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            output.Write(
                key + " " + i.ToString(CultureInfo.InvariantCulture) + " " + names[i] + "\n");
        }
    }

    /// <summary>
    /// Runs one of the dump modes by name, matching the C oracle's command line.
    /// </summary>
    /// <returns>A process exit code: 0 on success, 2 on misuse.</returns>
    public static int Run(TextWriter output, TextWriter error, string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Length == 0)
        {
            return Usage(error);
        }

        switch (arguments[0])
        {
            case "rng":
                if (arguments.Length != 3
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint seed)
                    || !long.TryParse(arguments[2], CultureInfo.InvariantCulture, out long count))
                {
                    return Usage(error);
                }

                DumpRng(output, seed, count);
                return 0;

            // These wait on the code they exist to check. Reporting that plainly
            // beats emitting a dump that would silently compare nothing.
            case "seeds":
                if (arguments.Length != 2
                    || !uint.TryParse(arguments[1], CultureInfo.InvariantCulture, out uint seedsSeed))
                {
                    return Usage(error);
                }

                DumpSeeds(output, seedsSeed);
                return 0;

            case "cave":
                error.WriteLine(
                    "oracle: 'cave' needs the dungeon generator, which is not ported yet.");
                return 3;

            default:
                return Usage(error);
        }
    }

    private static int Usage(TextWriter error)
    {
        error.WriteLine("usage:");
        error.WriteLine("  airom oracle rng   <seed> <count>   raw generator values");
        error.WriteLine("  airom oracle seeds <seed>           seeding chain and magic_init");
        error.WriteLine("  airom oracle cave  <seed> <level>   a generated dungeon level");
        return 2;
    }
}
