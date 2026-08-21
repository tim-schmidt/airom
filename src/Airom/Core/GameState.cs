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

    /// <summary>The character being played.</summary>
    public Player Player { get; set; } = new();

    /// <summary>What the player is carrying and wearing.</summary>
    public Inventory Inventory => _inventory ??= new Inventory(this);

    private Inventory? _inventory;

    /// <summary>What the player has worked out about the kinds of item they have met.</summary>
    public ItemKnowledge Knowledge { get; } = new();

    /// <summary>What the player has learned about the kinds of creature.</summary>
    public MonsterMemories Memories { get; } = new();

    /// <summary>How every item in the game is named.</summary>
    public ItemNames Names => _names ??= new ItemNames(Appearances, Knowledge);

    private ItemNames? _names;

    /// <summary>
    /// The player's own speed modifier, which every monster's speed is measured
    /// against. Zero for a fresh character.
    /// </summary>
    public int PlayerSpeed
    {
        get => Player.Speed;
        set => Player.Speed = value;
    }

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
    ///
    /// It starts at minus one because the loop counts a turn before playing it,
    /// which makes the first turn of a new game turn zero.
    /// </summary>
    public int Turn { get; set; } = -1;

    /// <summary>
    /// The player's race, which decides how each shop owner prices for them.
    /// </summary>
    public int PlayerRace
    {
        get => Player.Race;
        set => Player.Race = value;
    }

    /// <summary>The six town shops.</summary>
    public Stores Stores => _stores ??= new Stores(this);

    private Stores? _stores;

    /// <summary>Whether the player is carrying a lit light source.</summary>
    public bool PlayerLight { get; set; }

    /// <summary>
    /// Turns of fuel left in the light source. Umoria keeps this in the lamp
    /// itself, as inventory[INVEN_LIGHT].p1; it lives here until the inventory
    /// is ported, since the turn has to burn it either way.
    /// </summary>
    public int LightFuel { get; set; }

    /// <summary>
    /// Whether the player's own light is currently shining on the squares around
    /// them. Umoria's light_flag, which is switched off while running so a long
    /// run does not repaint the same nine squares every step.
    /// </summary>
    public bool TemporaryLightOn { get; set; }

    /// <summary>
    /// Whether the player is running rather than stepping. Umoria's find_flag,
    /// which the drawing reads as well as the loop.
    /// </summary>
    public bool Running { get; set; }

    /// <summary>
    /// Whether the player is drawn while running. A player option, off by
    /// default: leaving the character out makes a long run less flickery.
    /// </summary>
    public bool ShowSelfWhileRunning { get; set; }

    /// <summary>
    /// Whether picking something up is confirmed first. A player option, off by
    /// default: the prompt is for people who would rather not fill their pack
    /// with everything they walk over.
    /// </summary>
    public bool PromptBeforeCarrying { get; set; }

    /// <summary>
    /// Whether a run cuts a known corner rather than going the long way round. A
    /// player option, on by default.
    /// </summary>
    public bool CutCorners { get; set; } = true;

    /// <summary>
    /// Whether a run examines a possible corner rather than stopping at it. A
    /// player option, on by default.
    /// </summary>
    public bool ExamineCorners { get; set; } = true;

    /// <summary>
    /// Whether a run carries on past an open door. A player option, off by
    /// default, since a doorway is usually worth stopping at.
    /// </summary>
    public bool IgnoreDoorsWhileRunning { get; set; }

    /// <summary>
    /// Whether a run stops when the map scrolls to a new sector. Umoria's
    /// find_bound.
    /// </summary>
    public bool StopAtLevelBounds { get; set; }

    /// <summary>
    /// Whether the rogue-like key set is in use. A player option, off by
    /// default, which decides how a typed key is translated before dispatch.
    /// </summary>
    public bool RogueLikeCommands { get; set; }

    /// <summary>Whether repeat and rest counts are shown. A player option.</summary>
    public bool DisplayCounts { get; set; } = true;

    /// <summary>
    /// Whether the inventory screens show what each thing weighs. A player
    /// option, off by default; turning it on narrows the room left for the
    /// descriptions themselves.
    /// </summary>
    public bool ShowWeights { get; set; }

    /// <summary>
    /// Whether a character has been rolled yet. The character sheet is drawn
    /// during creation as well as after it, and before the rolling is done
    /// there is nothing to put in it. Mirrors character_generated.
    /// </summary>
    public bool CharacterGenerated { get; set; }

    /// <summary>
    /// When the character was rolled, as a count of seconds. With no user ids
    /// to tell one player's characters from another's, this is what identifies
    /// a character in the score table. Mirrors birth_date.
    /// </summary>
    public int BirthDate { get; set; }

    /// <summary>
    /// Whether this game was restored from a panic save - one written when
    /// something went wrong rather than when the player asked. Such a game is
    /// never scored. Mirrors panic_save.
    /// </summary>
    public bool PanicSaved { get; set; }

    /// <summary>
    /// Whether the character has been saved to disk. Mirrors character_saved.
    /// </summary>
    public bool CharacterSaved { get; set; }

    /// <summary>Whether the player has won and been made king. Mirrors total_winner.</summary>
    public bool TotalWinnerCrowned { get; set; }

    /// <summary>What killed the player, as the tomb and the score table say it.</summary>
    public string DiedFrom { get; set; } = string.Empty;

    /// <summary>
    /// The best score this character has ever had, so that a score cannot fall
    /// between one save and the next. Mirrors max_score.
    /// </summary>
    public int MaxScore { get; set; }

    /// <summary>Whether wizard mode is active, which forfeits the score.</summary>
    public bool Wizard { get; set; }

    /// <summary>
    /// Why the score will not count: 1 resurrected, 2 wizard, 4 duplicate.
    /// Mirrors Umoria's noscore.
    /// </summary>
    public int NoScore { get; set; }

    /// <summary>Whether the terminal bell sounds. A player option, on by default.</summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>
    /// Whether mineral veins are drawn differently from plain rock. A player
    /// option, off by default.
    /// </summary>
    public bool HighlightSeams { get; set; }

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
    /// <summary>
    /// Puts back the two seeds a saved game carries, so the town and every
    /// shuffled appearance come out the way they did before. Mirrors reading
    /// randes_seed and town_seed in get_char().
    /// </summary>
    public void SetSeeds(uint randomEssence, uint town)
    {
        RandesSeed = randomEssence;
        TownSeed = town;
    }

    public void MagicInit() => Appearances.Initialize(Rng, RandesSeed);
}
