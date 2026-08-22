// Ported from the owner_type struct in Umoria 5.6 source/types.h.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>
/// A store owner's pricing and haggling temperament. Mirrors Umoria's
/// owner_type.
/// </summary>
/// <param name="name">
/// Display name, which also carries the owner's race and store in fixed
/// columns - "Mauglin the Grumpy     (Dwarf)      Armory".
/// </param>
/// <param name="maxCost">Most the owner will pay for a single item.</param>
/// <param name="maxInflate">Highest markup percentage when selling.</param>
/// <param name="minInflate">Lowest markup percentage when selling.</param>
/// <param name="hagglePercent">How much ground the owner gives per haggling round.</param>
/// <param name="ownerRace">
/// Index into the race table, used with <see cref="GameTables.RaceGoldAdjust"/>
/// to price by the player's race.
/// </param>
/// <param name="insultMax">Insults tolerated before the owner throws the player out.</param>
public sealed class OwnerType(
    string name,
    short maxCost,
    byte maxInflate,
    byte minInflate,
    byte hagglePercent,
    byte ownerRace,
    byte insultMax)
{
    public string Name { get; } = name;

    public short MaxCost { get; } = maxCost;

    public byte MaxInflate { get; } = maxInflate;

    public byte MinInflate { get; } = minInflate;

    public byte HagglePercent { get; } = hagglePercent;

    public byte OwnerRace { get; } = ownerRace;

    public byte InsultMax { get; } = insultMax;

    public override string ToString() => Name;
}
