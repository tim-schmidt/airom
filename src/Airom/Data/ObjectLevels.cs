// Ported from init_t_level() in Umoria 5.6 source/main.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>
/// An index of the object table sorted by the depth an item first appears at.
///
/// Item generation needs to ask "give me something found at or above level n"
/// cheaply. Rather than scan the table, Umoria builds this once at startup: a
/// running count of how many objects exist up to each level, and the object
/// indices themselves in level order. Picking then costs one draw and one
/// lookup.
///
/// Mirrors init_t_level(), including its counting sort - which is why the
/// result is not stable, and why nothing should depend on the order within a
/// level beyond what the original produces.
/// </summary>
public static class ObjectLevels
{
    /// <summary>Umoria's MAX_OBJ_LEVEL: deepest level items are indexed for.</summary>
    public const int MaxObjectLevel = 50;

    /// <summary>
    /// Umoria's MAX_DUNGEON_OBJ. Only the dungeon objects are indexed - the
    /// rows above this are doors, stairs, rubble and gold, which are placed
    /// deliberately rather than drawn.
    /// </summary>
    public const int DungeonObjectCount = 344;

    private static readonly int[] _levelTotals = new int[MaxObjectLevel + 1];
    private static readonly int[] _sorted = new int[DungeonObjectCount];

    static ObjectLevels() => Build();

    /// <summary>
    /// Running totals: <c>LevelTotals[n]</c> is how many objects exist at level
    /// n or shallower, so the objects available at level n occupy
    /// <c>Sorted[0 .. LevelTotals[n] - 1]</c>.
    /// </summary>
    public static ReadOnlySpan<int> LevelTotals => _levelTotals;

    /// <summary>Object table indices, ordered by the level they appear at.</summary>
    public static ReadOnlySpan<int> Sorted => _sorted;

    private static void Build()
    {
        Array.Clear(_levelTotals);

        for (int i = 0; i < DungeonObjectCount; i++)
        {
            _levelTotals[GameTables.ObjectList[i].Level]++;
        }

        for (int i = 1; i <= MaxObjectLevel; i++)
        {
            _levelTotals[i] += _levelTotals[i - 1];
        }

        // Counting sort: each level's slots are filled from the top down, which
        // is what makes this O(n) and also what makes it unstable.
        var placed = new int[MaxObjectLevel + 1];
        Array.Fill(placed, 1);

        for (int i = 0; i < DungeonObjectCount; i++)
        {
            int level = GameTables.ObjectList[i].Level;
            _sorted[_levelTotals[level] - placed[level]] = i;
            placed[level]++;
        }
    }
}
