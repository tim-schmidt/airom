// Ported from init_seeds() in Umoria 5.6 source/misc1.c, and the seed globals
// in source/variable.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Core;

/// <summary>
/// State belonging to one game in progress.
///
/// Umoria keeps this in about 130 globals. Gathering them onto an instance is
/// the one structural change the port makes here, and it is made for a specific
/// reason rather than tidiness: verification runs two games side by side and
/// compares them, which ambient global state makes awkward.
/// </summary>
public sealed class GameState
{
    /// <summary>The generator every random decision in the game comes from.</summary>
    public Rng Rng { get; } = new();

    /// <summary>
    /// Seed for the item appearance shuffle, stored in save files so a
    /// character's potions keep looking the same across sessions.
    /// </summary>
    public uint RandesSeed { get; private set; }

    /// <summary>
    /// Seed for the town layout, stored in save files so the town is rebuilt
    /// identically every time the player climbs out of the dungeon.
    /// </summary>
    public uint TownSeed { get; private set; }

    /// <summary>This game's randomised item appearances.</summary>
    public Appearances Appearances { get; } = new();

    /// <summary>
    /// Derives the appearance seed, the town seed and the main generator state
    /// from one value. Mirrors init_seeds().
    ///
    /// The offsets are arbitrary constants from the original; what matters is
    /// that all three streams are decided by a single number, which is what lets
    /// a whole game be reproduced from one seed.
    ///
    /// A seed of zero means "pick one from the clock", as in the C. Pass a
    /// non-zero seed for anything that has to be repeatable.
    /// </summary>
    public void InitSeeds(uint seed)
    {
        uint value = seed != 0
            ? seed
            : unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        RandesSeed = value;

        value += 8762;
        TownSeed = value;

        value += 113452;
        Rng.SetSeed(value);

        // The original burns a random number of draws here, commented "make it
        // a little more random". It does not add entropy - the count is drawn
        // from the same generator - but it does shift the stream, so it has to
        // be reproduced exactly.
        for (int remaining = Rng.RandInt(100); remaining != 0; remaining--)
        {
            Rng.Next();
        }
    }

    /// <summary>
    /// Randomises the item appearances for this game. Mirrors magic_init().
    /// Must be called after <see cref="InitSeeds"/>, which supplies its seed.
    /// </summary>
    public void MagicInit() => Appearances.Initialize(Rng, RandesSeed);
}
