// Ported from race_type, class_type and background_type in Umoria 5.6
// source/types.h.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>The six stats, in the order they are stored.</summary>
public static class Stat
{
    public const int Strength = 0;
    public const int Intelligence = 1;
    public const int Wisdom = 2;
    public const int Dexterity = 3;
    public const int Constitution = 4;
    public const int Charisma = 5;

    public const int Count = 6;
}

/// <summary>Which kind of magic a class uses, if any. Umoria's NONE/MAGE/PRIEST.</summary>
public static class SpellRealm
{
    public const int None = 0;
    public const int Mage = 1;
    public const int Priest = 2;
}

/// <summary>
/// One playable race. Mirrors Umoria's race_type.
///
/// The stat adjustments are applied to the rolled values rather than replacing
/// them, so a race shifts a character rather than defining one.
/// </summary>
public sealed class RaceType(
    string name,
    short strengthAdjust,
    short intelligenceAdjust,
    short wisdomAdjust,
    short dexterityAdjust,
    short constitutionAdjust,
    short charismaAdjust,
    byte baseAge,
    byte ageRange,
    byte maleBaseHeight,
    byte maleHeightRange,
    byte maleBaseWeight,
    byte maleWeightRange,
    byte femaleBaseHeight,
    byte femaleHeightRange,
    byte femaleBaseWeight,
    byte femaleWeightRange,
    short baseDisarm,
    short search,
    short stealth,
    short searchFrequency,
    short baseToHit,
    short baseToHitBows,
    short baseSave,
    byte baseHitDie,
    byte infravision,
    byte experienceFactor,
    byte allowedClasses)
{
    public string Name { get; } = name;

    /// <summary>Stat adjustments, indexed by <see cref="Stat"/>.</summary>
    public short[] StatAdjust { get; } =
    [
        strengthAdjust, intelligenceAdjust, wisdomAdjust,
        dexterityAdjust, constitutionAdjust, charismaAdjust,
    ];

    public byte BaseAge { get; } = baseAge;

    public byte AgeRange { get; } = ageRange;

    public byte MaleBaseHeight { get; } = maleBaseHeight;

    public byte MaleHeightRange { get; } = maleHeightRange;

    public byte MaleBaseWeight { get; } = maleBaseWeight;

    public byte MaleWeightRange { get; } = maleWeightRange;

    public byte FemaleBaseHeight { get; } = femaleBaseHeight;

    public byte FemaleHeightRange { get; } = femaleHeightRange;

    public byte FemaleBaseWeight { get; } = femaleBaseWeight;

    public byte FemaleWeightRange { get; } = femaleWeightRange;

    public short BaseDisarm { get; } = baseDisarm;

    public short Search { get; } = search;

    public short Stealth { get; } = stealth;

    public short SearchFrequency { get; } = searchFrequency;

    public short BaseToHit { get; } = baseToHit;

    public short BaseToHitBows { get; } = baseToHitBows;

    public short BaseSave { get; } = baseSave;

    public byte BaseHitDie { get; } = baseHitDie;

    public byte Infravision { get; } = infravision;

    /// <summary>How much slower this race levels, as a percentage.</summary>
    public byte ExperienceFactor { get; } = experienceFactor;

    /// <summary>Bit field of the classes this race may take.</summary>
    public byte AllowedClasses { get; } = allowedClasses;

    public override string ToString() => Name;
}

/// <summary>
/// One character class. Mirrors Umoria's class_type.
///
/// Its stat adjustments are applied after the race's, so a class can pull back
/// down what a race pushed up - which is why a Half-Troll Mage is possible but
/// not advisable.
/// </summary>
public sealed class ClassType(
    string title,
    byte hitDieAdjust,
    byte disarm,
    byte search,
    byte stealth,
    byte searchFrequency,
    byte baseToHit,
    byte baseToHitBows,
    byte save,
    short strengthAdjust,
    short intelligenceAdjust,
    short wisdomAdjust,
    short dexterityAdjust,
    short constitutionAdjust,
    short charismaAdjust,
    byte spellRealm,
    byte experienceFactor,
    byte firstSpellLevel)
{
    public string Title { get; } = title;

    public byte HitDieAdjust { get; } = hitDieAdjust;

    public byte Disarm { get; } = disarm;

    public byte Search { get; } = search;

    public byte Stealth { get; } = stealth;

    public byte SearchFrequency { get; } = searchFrequency;

    public byte BaseToHit { get; } = baseToHit;

    public byte BaseToHitBows { get; } = baseToHitBows;

    public byte Save { get; } = save;

    /// <summary>Stat adjustments, indexed by <see cref="Stat"/>.</summary>
    public short[] StatAdjust { get; } =
    [
        strengthAdjust, intelligenceAdjust, wisdomAdjust,
        dexterityAdjust, constitutionAdjust, charismaAdjust,
    ];

    /// <summary>One of the <see cref="SpellRealm"/> values.</summary>
    public byte SpellRealm { get; } = spellRealm;

    public byte ExperienceFactor { get; } = experienceFactor;

    public byte FirstSpellLevel { get; } = firstSpellLevel;

    public override string ToString() => Title;
}

/// <summary>
/// One fragment of a character's history. Mirrors Umoria's background_type.
///
/// The table is a set of linked charts rather than a flat list: an entry names
/// the chart it belongs to and the chart to continue with, so rolling a history
/// walks from one chart to the next until it runs out.
/// </summary>
public sealed class BackgroundType(string text, byte roll, byte chart, byte next, byte bonus)
{
    public string Text { get; } = text;

    /// <summary>Highest percentile roll that selects this entry within its chart.</summary>
    public byte Roll { get; } = roll;

    /// <summary>Which chart this entry belongs to.</summary>
    public byte Chart { get; } = chart;

    /// <summary>Chart to continue with, or 0 to stop.</summary>
    public byte Next { get; } = next;

    /// <summary>Social class contribution, offset by 50.</summary>
    public byte Bonus { get; } = bonus;

    public override string ToString() => Text;
}

/// <summary>
/// One spell as a class knows it. Mirrors Umoria's spell_type.
///
/// The same thirty-one entries mean different spells for different classes: a
/// rogue's fourth spell is not a mage's, and where a class never learns a spell
/// at all the level is set to 99 so that nothing can reach it.
/// </summary>
public sealed class SpellType(byte level, byte mana, byte fail, byte experience)
{
    /// <summary>Character level at which the spell can be learned; 99 for never.</summary>
    public byte Level { get; } = level;

    public byte Mana { get; } = mana;

    /// <summary>Base percentage chance of failure, before level and stat.</summary>
    public byte Fail { get; } = fail;

    /// <summary>A quarter of the experience gained for first casting it.</summary>
    public byte Experience { get; } = experience;
}
