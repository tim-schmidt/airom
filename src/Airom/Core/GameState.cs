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
    /// The level currently being played. Sized for a dungeon level to begin
    /// with; the town is smaller and resizes it.
    /// </summary>
    public Cave Cave { get; } = new(DungeonHeight, DungeonWidth);

    /// <summary>Objects lying on the current level.</summary>
    public ObjectPool Objects { get; } = new();

    /// <summary>Monsters on the current level.</summary>
    public MonsterPool Monsters { get; } = new();

    /// <summary>
    /// The player's own speed modifier, which every monster's speed is measured
    /// against. Zero for a fresh character.
    /// </summary>
    public int PlayerSpeed { get; set; }

    /// <summary>
    /// Depth in the dungeon; 0 is the town. Feeds the difficulty of everything
    /// generated, so it must be set before a level is carved.
    /// </summary>
    public int DungeonLevel { get; set; }

    /// <summary>
    /// Serial number stamped into each pile of missiles, so two otherwise
    /// identical piles do not merge in the inventory. It wraps rather than
    /// saturating, which is why it is signed and allowed to go negative.
    /// </summary>
    public int MissileCounter { get; set; }

    /// <summary>
    /// Where the player stands. Scattered objects avoid this square, so it has
    /// to be decided before the level is populated. Negative means unplaced.
    /// </summary>
    public int CharacterRow { get; set; } = -1;

    /// <inheritdoc cref="CharacterRow"/>
    public int CharacterColumn { get; set; } = -1;

    /// <summary>
    /// Turns elapsed. The town's day and night alternate in blocks of 5000, so
    /// this decides which one the player walks out into.
    /// </summary>
    public int Turn { get; set; }

    /// <summary>
    /// The player's race, which decides how each shop owner prices for them.
    /// </summary>
    public int PlayerRace { get; set; }

    /// <summary>The six town shops.</summary>
    public Stores Stores => _stores ??= new Stores(this);

    private Stores? _stores;

    /// <summary>Whether the player has already won, which stops the win monsters respawning.</summary>
    public bool TotalWinner { get; set; }

    /// <summary>Turns freshly generated items into specific enchanted ones.</summary>
    public Enchantment Enchantment => _enchantment ??= new Enchantment(this);

    private Enchantment? _enchantment;

    /// <summary>Umoria's MAX_HEIGHT: rows in a dungeon level.</summary>
    public const int DungeonHeight = 66;

    /// <summary>Umoria's MAX_WIDTH: columns in a dungeon level.</summary>
    public const int DungeonWidth = 198;

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
