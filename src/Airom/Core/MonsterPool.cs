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
    /// <summary>Copies another monster's whole state into this one.</summary>
    public void CopyStateFrom(Monster other)
    {
        ArgumentNullException.ThrowIfNull(other);

        CreatureIndex = other.CreatureIndex;
        HitPoints = other.HitPoints;
        Sleep = other.Sleep;
        Speed = other.Speed;
        Row = other.Row;
        Column = other.Column;
        DistanceToPlayer = other.DistanceToPlayer;
        Visible = other.Visible;
        Stunned = other.Stunned;
        Confused = other.Confused;
    }

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

    /// <summary>Puts the mark back where a saved game left it.</summary>
    internal void SetCount(int count) => Count = count;

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
    /// Which monster creatures() is part way through, or -1 when nothing is
    /// scanning. Mirrors hack_monptr, which the original calls a horrible hack
    /// and which decides whether a death can be finished at once.
    /// </summary>
    public int ScanIndex { get; set; } = -1;

    /// <summary>
    /// How many monsters have been bred on this level, which is what stops a
    /// breeder filling it. Umoria's mon_tot_mult.
    /// </summary>
    public int BredCount { get; set; }

    /// <summary>
    /// Removes a monster outright. Mirrors delete_monster().
    ///
    /// The last monster in the list is moved down into the hole, so the list
    /// stays packed - which is why the square it came from has to be repointed.
    /// </summary>
    public void Delete(int index, Cave cave, Lighting? lighting)
    {
        ArgumentNullException.ThrowIfNull(cave);

        MarkDead(index, cave, lighting);
        Compact(index, cave);
    }

    /// <summary>
    /// Takes a monster off the map without removing its record. Mirrors
    /// fix1_delete_monster().
    ///
    /// creatures() scans the list as it runs, and deleting an entry from under
    /// it would give another monster two turns, so a death that happens during
    /// the scan is done in two halves.
    /// </summary>
    public void MarkDead(int index, Cave cave, Lighting? lighting)
    {
        ArgumentNullException.ThrowIfNull(cave);

        Monster monster = _monsters[index];

        // Forced negative so the monster is certainly dead: something that has
        // just been eaten may still have hit points.
        monster.HitPoints = -1;
        cave[monster.Row, monster.Column].MonsterIndex = 0;

        if (monster.Visible)
        {
            lighting?.LightSpot(monster.Row, monster.Column);
        }

        if (BredCount > 0)
        {
            BredCount--;
        }
    }

    /// <summary>
    /// Closes the hole a dead monster left. Mirrors fix2_delete_monster().
    /// </summary>
    public void Compact(int index, Cave cave)
    {
        ArgumentNullException.ThrowIfNull(cave);

        int last = Count - 1;

        if (index != last)
        {
            Monster moved = _monsters[last];
            cave[moved.Row, moved.Column].MonsterIndex = index;
            _monsters[index].CopyStateFrom(moved);
        }

        _monsters[last].Clear();
        Count--;
    }

    /// <summary>
    /// What makes room when the list is full. Mirrors compact_monsters(), which
    /// popm() calls before it gives up. Attached by <see cref="Compaction"/>.
    /// </summary>
    public Func<bool>? Compactor { get; set; }

    /// <summary>
    /// Claims the next free slot. Mirrors popm().
    ///
    /// A full list is compacted first, and compaction can fail - the Balrog is
    /// never thrown away, and neither is whatever the monster loop is part way
    /// through. When it does, this fails too, which is what a monster failing
    /// to arrive looks like from the inside.
    /// </summary>
    /// <returns>The slot, or -1 when there is no room to be had.</returns>
    public int Allocate()
    {
        if (Count == Capacity)
        {
            if (Compactor is null)
            {
                throw new InvalidOperationException(
                    "The monster list is full and nothing is attached to compact it.");
            }

            if (!Compactor())
            {
                return -1;
            }
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
