// Ported from magic_init() in Umoria 5.6 source/desc.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Text;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The randomised appearances of unidentified items for one game: which colour
/// a potion looks like, which wood a staff is made of, what nonsense title is
/// printed on a scroll.
///
/// Umoria shuffles the global name tables in place. Here the pristine tables in
/// <see cref="GameTables"/> stay untouched and each game gets its own copy. The
/// shuffles consume the generator in exactly the same order, so the observable
/// result is identical - it just cannot leak between games, or between tests.
/// </summary>
public sealed class Appearances
{
    /// <summary>Umoria's MAX_TITLES: how many scroll titles are made up.</summary>
    public const int TitleCount = 45;

    /// <summary>How many mushroom appearances there are. Umoria's MAX_MUSH.</summary>
    public const int MushroomCount = 22;

    /// <summary>
    /// Scroll titles are stored in a char[10] in the C, so nine characters plus
    /// a terminator is the most that survives.
    /// </summary>
    private const int MaxTitleLength = 9;

    public Appearances()
    {
        Colors = [.. GameTables.Colors];
        Mushrooms = [.. GameTables.Mushrooms];
        Woods = [.. GameTables.Woods];
        Metals = [.. GameTables.Metals];
        Rocks = [.. GameTables.Rocks];
        Amulets = [.. GameTables.Amulets];
        Titles = new string[TitleCount];
        Array.Fill(Titles, string.Empty);
    }

    /// <summary>Potion appearances. The first three are fixed, never shuffled.</summary>
    public string[] Colors { get; }

    public string[] Mushrooms { get; }

    public string[] Woods { get; }

    public string[] Metals { get; }

    public string[] Rocks { get; }

    public string[] Amulets { get; }

    /// <summary>Made-up titles printed on unidentified scrolls.</summary>
    public string[] Titles { get; }

    /// <summary>
    /// Randomises every appearance. Mirrors magic_init().
    ///
    /// The whole thing runs inside a push/pop of the generator seeded from
    /// <paramref name="randesSeed"/>, so appearances are reproducible for a
    /// given character while the main sequence carries on elsewhere, and picks
    /// that sequence up again exactly where it was left.
    /// </summary>
    public void Initialize(Rng rng, uint randesSeed)
    {
        ArgumentNullException.ThrowIfNull(rng);

        rng.PushSeed(randesSeed);

        // The first three potions are slime mould juice, apple juice and water,
        // which always look like themselves. Everything from index 3 up swaps
        // only within that range.
        for (int i = 3; i < Colors.Length; i++)
        {
            int j = rng.RandInt(Colors.Length - 3) + 2;
            (Colors[i], Colors[j]) = (Colors[j], Colors[i]);
        }

        // Order matters: each shuffle consumes draws, so doing these in a
        // different sequence would change every appearance downstream.
        Shuffle(rng, Woods);
        Shuffle(rng, Metals);
        Shuffle(rng, Rocks);
        Shuffle(rng, Amulets);
        Shuffle(rng, Mushrooms);

        BuildTitles(rng);

        rng.PopSeed();
    }

    /// <summary>
    /// Umoria's shuffle: walk the array, swapping each entry with one drawn at
    /// random - including, sometimes, itself. Not a uniform shuffle, but
    /// reproducing it exactly is the point.
    /// </summary>
    private static void Shuffle(Rng rng, string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            int j = rng.RandInt(names.Length) - 1;
            (names[i], names[j]) = (names[j], names[i]);
        }
    }

    private void BuildTitles(Rng rng)
    {
        var built = new StringBuilder();

        for (int h = 0; h < TitleCount; h++)
        {
            built.Clear();

            int words = rng.RandInt(2) + 1;
            for (int word = 0; word < words; word++)
            {
                for (int syllable = rng.RandInt(2); syllable > 0; syllable--)
                {
                    built.Append(GameTables.Syllables[rng.RandInt(GameTables.Syllables.Length) - 1]);
                }

                if (word < words - 1)
                {
                    built.Append(' ');
                }
            }

            Titles[h] = Truncate(built.ToString());
        }
    }

    /// <summary>
    /// Cuts a built title down to what fits the C's char[10].
    ///
    /// The original writes a terminator at index 8 or 9 depending on whether
    /// index 8 holds a space, and does so whatever the string's length - reading
    /// past the terminator when the title is short. That read is harmless: both
    /// branches write at or beyond the existing terminator, so the copied result
    /// is the untruncated title. What is left is simply "keep nine characters,
    /// or eight if the ninth is a space", which is what avoids a title ending on
    /// a stranded space.
    /// </summary>
    private static string Truncate(string built)
    {
        if (built.Length > MaxTitleLength - 1 && built[MaxTitleLength - 1] == ' ')
        {
            return built[..(MaxTitleLength - 1)];
        }

        return built.Length > MaxTitleLength ? built[..MaxTitleLength] : built;
    }
}
