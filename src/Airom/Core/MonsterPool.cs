// Ported from monster_type in Umoria 5.6 source/types.h, popm() in
// source/misc1.c, and init_m_level() in source/main.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// A live monster on the current level, as opposed to the table row it was
/// spawned from. Mirrors Umoria's monster_type.
/// </summary>
public sealed class Monster
{
    /// <summary>Row in <see cref="GameTables.CreatureList"/> this was spawned from.</summary>
    public int CreatureIndex { get; set; }

    public int HitPoints { get; set; }

    /// <summary>Turns left asleep. Zero means awake and hunting.</summary>
    public int Sleep { get; set; }

    /// <summary>
    /// Speed relative to normal. The creature table stores this offset by ten so
    /// it fits a byte; here it is already unpacked, so zero is normal pace.
    /// </summary>
    public int Speed { get; set; }

    /// <summary>Row on the level. Widened from the C's byte.</summary>
    public int Row { get; set; }

    /// <summary>Column on the level. Widened from the C's byte.</summary>
    public int Column { get; set; }

    /// <summary>Current distance from the player, kept so the AI need not recompute it.</summary>
    public int DistanceToPlayer { get; set; }

    /// <summary>Whether the player can currently see it.</summary>
    public bool Visible { get; set; }

    public int Stunned { get; set; }

    public int Confused { get; set; }

    /// <summary>Resets to the blank_monster state Umoria clears the list to.</summary>
    public void Clear()
    {
        CreatureIndex = 0;
        HitPoints = 0;
        Sleep = 0;
        Speed = 0;
        Row = 0;
        Column = 0;
        DistanceToPlayer = 0;
        Visible = false;
        Stunned = 0;
        Confused = 0;
    }
}

/// <summary>
/// The monsters on the current level. Mirrors Umoria's m_list with its mfptr
/// high-water mark.
/// </summary>
public sealed class MonsterPool
{
    /// <summary>
    /// Umoria's MIN_MONIX. Index 0 means "no monster" and index 1 is reserved
    /// for the player, so real monsters start at 2.
    /// </summary>
    public const int FirstIndex = 2;

    /// <summary>Umoria's MAX_MALLOC: monsters allowed on one level.</summary>
    public const int Capacity = 125;

    private readonly Monster[] _monsters;

    public MonsterPool()
    {
        _monsters = new Monster[Capacity];
        for (int i = 0; i < Capacity; i++)
        {
            _monsters[i] = new Monster();
        }

        Count = FirstIndex;
    }

    /// <summary>High-water mark. Umoria's mfptr.</summary>
    public int Count { get; private set; }

    public Monster this[int index] => _monsters[index];

    /// <summary>Clears the list for a new level. Mirrors mlink().</summary>
    public void Reset()
    {
        foreach (Monster monster in _monsters)
        {
            monster.Clear();
        }

        Count = FirstIndex;
    }

    /// <summary>
    /// Claims the next free slot. Mirrors popm().
    ///
    /// Umoria calls compact_monsters() when the list fills, deleting distant
    /// ones to make room, and place_monster simply fails if that cannot help.
    /// Compaction is not ported yet, so this throws rather than failing quietly:
    /// a silently missing monster would diverge from the original with no
    /// visible symptom.
    /// </summary>
    public int Allocate()
    {
        if (Count == Capacity)
        {
            throw new NotSupportedException(
                "Monster list is full and compact_monsters() is not ported yet.");
        }

        return Count++;
    }
}

/// <summary>
/// An index of the creature table by the depth a monster first appears at.
/// Mirrors init_m_level() in main.c.
///
/// Unlike the object index this needs no sorted array: the creature table is
/// already in level order, so a running count of how many exist up to each level
/// is enough to draw from.
/// </summary>
public static class MonsterLevels
{
    /// <summary>Umoria's MAX_MONS_LEVEL: deepest level monsters are indexed for.</summary>
    public const int MaxMonsterLevel = 40;

    /// <summary>
    /// Umoria's WIN_MON_TOT. The last two creatures in the table win the game
    /// and are placed deliberately, never drawn, so they are left out of the
    /// index entirely.
    /// </summary>
    public const int WinMonsterCount = 2;

    private static readonly int[] _levelTotals = new int[MaxMonsterLevel + 1];

    static MonsterLevels() => Build();

    /// <summary>
    /// Running totals: <c>LevelTotals[n]</c> is how many monsters exist at level
    /// n or shallower.
    /// </summary>
    public static ReadOnlySpan<int> LevelTotals => _levelTotals;

    private static void Build()
    {
        Array.Clear(_levelTotals);

        int drawable = GameTables.CreatureList.Length - WinMonsterCount;
        for (int i = 0; i < drawable; i++)
        {
            _levelTotals[GameTables.CreatureList[i].Level]++;
        }

        for (int i = 1; i <= MaxMonsterLevel; i++)
        {
            _levelTotals[i] += _levelTotals[i - 1];
        }
    }
}
