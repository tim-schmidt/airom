// Ported from the creature_type struct in Umoria 5.6 source/types.h.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>
/// One row of the monster table - the template from which a live monster is
/// spawned. Mirrors Umoria's creature_type.
///
/// As with <see cref="TreasureType"/> the original integer widths are kept, so
/// that a value the C struct could not have held fails to compile rather than
/// wrapping silently.
/// </summary>
/// <param name="name">Display name.</param>
/// <param name="moveFlags">
/// Movement and behaviour bits (Umoria's cmove). Also carries the "wins the
/// game when killed" bit, which only the Balrog has set.
/// </param>
/// <param name="spellFlags">Spell and breath-attack bits (Umoria's spells).</param>
/// <param name="defenseFlags">
/// Resistance, immunity and creature-kind bits (Umoria's cdefense).
/// </param>
/// <param name="killExperience">Experience awarded for a kill, before level scaling.</param>
/// <param name="sleep">Sleep counter; higher means less alert.</param>
/// <param name="areaOfEffect">Radius within which the monster notices the player.</param>
/// <param name="ac">Armour class.</param>
/// <param name="speed">
/// Movement speed, stored with an offset of +10 - so 11 is normal speed and 13
/// is three steps faster than the player.
/// </param>
/// <param name="displayChar">Map glyph.</param>
/// <param name="hitDiceCount">Number of hit dice.</param>
/// <param name="hitDiceSides">Sides per hit die.</param>
/// <param name="attacks">
/// Up to four indices into the monster attack table; 0 means "no attack in this
/// slot". Always four entries, matching the C damage[4].
/// </param>
/// <param name="level">Dungeon level on which the monster normally appears.</param>
public sealed class CreatureType(
    string name,
    uint moveFlags,
    uint spellFlags,
    ushort defenseFlags,
    ushort killExperience,
    byte sleep,
    byte areaOfEffect,
    byte ac,
    byte speed,
    char displayChar,
    byte hitDiceCount,
    byte hitDiceSides,
    byte[] attacks,
    byte level)
{
    public string Name { get; } = name;

    public uint MoveFlags { get; } = moveFlags;

    public uint SpellFlags { get; } = spellFlags;

    public ushort DefenseFlags { get; } = defenseFlags;

    public ushort KillExperience { get; } = killExperience;

    public byte Sleep { get; } = sleep;

    public byte AreaOfEffect { get; } = areaOfEffect;

    public byte Ac { get; } = ac;

    public byte Speed { get; } = speed;

    public char DisplayChar { get; } = displayChar;

    public byte HitDiceCount { get; } = hitDiceCount;

    public byte HitDiceSides { get; } = hitDiceSides;

    private readonly byte[] _attacks = attacks;

    /// <summary>The four attack-table indices, 0 where the slot is unused.</summary>
    public ReadOnlySpan<byte> Attacks => _attacks;

    public byte Level { get; } = level;

    public override string ToString() => Name;
}
