// Ported from the monster memory record in Umoria 5.6 source/types.h, which
// source/moria3.c and source/creature.c write as the player learns things.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// What the player has learned about one kind of creature. Mirrors
/// recall_type.
///
/// Nothing here is known in advance: every field is filled in by fighting the
/// thing. Seeing an orc drop a sword records that orcs carry objects; being
/// frozen by one records the breath. That is the point - the monster list is the
/// game's, and this is the player's copy of it, built the hard way.
/// </summary>
public sealed class MonsterMemory
{
    /// <summary>Movement and treasure flags seen. Mirrors r_cmove.</summary>
    public uint Move { get; set; }

    /// <summary>Spells and breaths seen. Mirrors r_spells.</summary>
    public uint Spells { get; set; }

    /// <summary>How many of this kind the player has killed.</summary>
    public int Kills { get; set; }

    /// <summary>How many times this kind has killed the player.</summary>
    public int Deaths { get; set; }

    /// <summary>Defences seen, such as being made of stone or never sleeping.</summary>
    public ushort Defense { get; set; }

    /// <summary>How often it has been seen to wake.</summary>
    public byte Wake { get; set; }

    /// <summary>How often it has been seen to ignore the player.</summary>
    public byte Ignore { get; set; }

    /// <summary>How often each of its attacks has been felt.</summary>
    public byte[] Attacks { get; } = new byte[MaxAttacks];

    /// <summary>How many attacks a creature can have. Umoria's MAX_MON_NATTACK.</summary>
    public const int MaxAttacks = 4;

    public void Clear()
    {
        Move = 0;
        Spells = 0;
        Kills = 0;
        Deaths = 0;
        Defense = 0;
        Wake = 0;
        Ignore = 0;
        Array.Clear(Attacks);
    }
}

/// <summary>
/// The player's copy of the monster list, one record per kind of creature.
/// Mirrors c_recall[].
/// </summary>
public sealed class MonsterMemories
{
    private readonly MonsterMemory[] _memories;

    public MonsterMemories()
    {
        _memories = new MonsterMemory[GameTables.CreatureList.Length];

        for (int i = 0; i < _memories.Length; i++)
        {
            _memories[i] = new MonsterMemory();
        }
    }

    public MonsterMemory this[int creatureIndex] => _memories[creatureIndex];

    public int Count => _memories.Length;

    /// <summary>Forgets everything, as starting a new game does.</summary>
    public void Reset()
    {
        foreach (MonsterMemory memory in _memories)
        {
            memory.Clear();
        }
    }
}
