// Ported from the object description half of Umoria 5.6 source/desc.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using System.Text;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// What the player has found out about the kinds of item they have met, as
/// opposed to about one particular item.
///
/// Umoria keeps two separate ideas here. Knowing an item's <em>kind</em> - that
/// the murky brown potion is cure light wounds - is learned once and applies to
/// every one the player ever finds, and is what this table records. Knowing an
/// item's <em>own</em> enchantment is per item and lives on the item itself.
/// </summary>
public sealed class ItemKnowledge
{
    /// <summary>The kind has been identified, so its real name is shown.</summary>
    public const byte Known = 0x2;

    /// <summary>The kind has been tried without being understood.</summary>
    public const byte Tried = 0x1;

    /// <summary>
    /// Seven kinds have shuffled appearances - amulets, rings, staves, wands,
    /// scrolls, potions and mushrooms - each with up to sixty-four varieties.
    /// </summary>
    private readonly byte[] _flags = new byte[7 * 64];

    /// <summary>
    /// Which appearance table an item belongs to, or -1 for something whose look
    /// gives it away. Mirrors object_offset().
    /// </summary>
    public static int OffsetOf(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        switch (item.TVal)
        {
            case ItemCategory.Amulet: return 0;
            case ItemCategory.Ring: return 1;
            case ItemCategory.Staff: return 2;
            case ItemCategory.Wand: return 3;
            case ItemCategory.Scroll1:
            case ItemCategory.Scroll2: return 4;
            case ItemCategory.Potion1:
            case ItemCategory.Potion2: return 5;
            case ItemCategory.Food:
                // Only the mushrooms have a shuffled appearance; ordinary food
                // looks like what it is.
                return (item.SubVal & (ItemCategory.SingleStackMin - 1)) < Appearances.MushroomCount
                    ? 6
                    : -1;
            default: return -1;
        }
    }

    private static int SlotOf(InvenType item, int offset) =>
        (offset << 6) + (item.SubVal & (ItemCategory.SingleStackMin - 1));

    /// <summary>Forgets every kind, as starting a new game does.</summary>
    public void Reset() => Array.Clear(_flags);

    /// <summary>Whether the kind is known. Mirrors known1_p().</summary>
    public bool IsKindKnown(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        int offset = OffsetOf(item);

        // Items with no shuffled appearance are always known, so that they sort
        // into a stable order in the pack.
        if (offset < 0)
        {
            return true;
        }

        if ((item.Identification & Identification.StoreBought) != 0)
        {
            return true;
        }

        return (_flags[SlotOf(item, offset)] & Known) != 0;
    }

    /// <summary>Learns what kind of thing this is. Mirrors known1().</summary>
    public void LearnKind(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        int offset = OffsetOf(item);
        if (offset < 0)
        {
            return;
        }

        int slot = SlotOf(item, offset);
        _flags[slot] |= Known;

        // Knowing it makes having tried it beside the point.
        _flags[slot] &= unchecked((byte)~Tried);
    }

    /// <summary>Records that the kind has been tried. Mirrors sample().</summary>
    public void MarkTried(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        int offset = OffsetOf(item);
        if (offset < 0)
        {
            return;
        }

        _flags[SlotOf(item, offset)] |= Tried;
    }

    /// <summary>
    /// Forgets everything about a kind. Nothing in the game does this - it is
    /// how the oracle asks for the same item to be named as though it had never
    /// been seen before.
    /// </summary>
    public void Forget(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        int offset = OffsetOf(item);
        if (offset >= 0)
        {
            _flags[SlotOf(item, offset)] = 0;
        }
    }

    /// <summary>Whether the kind has been tried and not understood.</summary>
    public bool WasTried(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        int offset = OffsetOf(item);
        return offset >= 0 && (_flags[SlotOf(item, offset)] & Tried) != 0;
    }

    /// <summary>
    /// Takes back an inscription the game added itself. Mirrors unsample().
    ///
    /// The "damned" mark deliberately survives: knowing an item is cursed is not
    /// something learning its name should undo.
    /// </summary>
    public void ClearGuesses(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.Identification &= unchecked((byte)~(Identification.Magik | Identification.Empty));

        int offset = OffsetOf(item);
        if (offset < 0)
        {
            return;
        }

        _flags[SlotOf(item, offset)] &= unchecked((byte)~Tried);
    }

    /// <summary>Learns this item's own enchantment. Mirrors known2().</summary>
    public void LearnEnchantment(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        ClearGuesses(item);
        item.Identification |= Identification.Known;
    }

    /// <summary>Whether this item's own enchantment is known. Mirrors known2_p().</summary>
    public static bool IsEnchantmentKnown(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return (item.Identification & Identification.Known) != 0;
    }

    /// <summary>Mirrors clear_known2().</summary>
    public static void ForgetEnchantment(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Identification &= unchecked((byte)~Identification.Known);
    }

    /// <summary>Mirrors clear_empty().</summary>
    public static void ClearEmpty(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Identification &= unchecked((byte)~Identification.Empty);
    }

    /// <summary>
    /// Marks an item as bought from a shop. Mirrors store_bought().
    ///
    /// A shop names what it sells, so nothing bought is ever a mystery.
    /// </summary>
    public void MarkStoreBought(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.Identification |= Identification.StoreBought;
        LearnEnchantment(item);
    }

    /// <summary>Mirrors store_bought_p().</summary>
    public static bool IsStoreBought(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return (item.Identification & Identification.StoreBought) != 0;
    }

    /// <summary>
    /// Whether an item is enchanted in a way the player has not noticed.
    /// Mirrors dungeon.c's enchanted().
    ///
    /// Only good enchantments count: a cursed item is not something the game
    /// hints at, and neither is one the player has already identified or
    /// guessed at.
    /// </summary>
    public static bool IsUnnoticedEnchantment(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.TVal < ItemCategory.MinEnchant || item.TVal > ItemCategory.MaxEnchant
            || (item.Flags & ItemFlags.Cursed) != 0)
        {
            return false;
        }

        if (IsEnchantmentKnown(item) || (item.Identification & Identification.Magik) != 0)
        {
            return false;
        }

        if (item.ToHit > 0 || item.ToDam > 0 || item.ToAc > 0)
        {
            return true;
        }

        // The flags that only mean something with a p1 behind them, and then
        // everything else worth noticing.
        if ((item.Flags & 0x4000107fu) != 0 && item.P1 > 0)
        {
            return true;
        }

        return (item.Flags & 0x07ffe980u) != 0;
    }

    /// <summary>Adds one of the game's own inscriptions. Mirrors add_inscribe().</summary>
    public static void AddInscription(InvenType item, byte flag)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Identification |= flag;
    }
}

/// <summary>
/// How an item's p1 field should be read, which depends entirely on what the
/// item is. Mirrors the constants at the top of objdes().
/// </summary>
internal enum P1Meaning
{
    Ignored,
    Charges,
    Plusses,
    Light,
    Flags,
    AlwaysPlusses,
}

/// <summary>
/// Naming things. Mirrors objdes().
///
/// One function builds every item name in the game, from "a Scroll titled 'nej
/// glen'" to "a Long Sword (2d5) (+7,+8) {magik}". What it can say depends on
/// what the player knows: an unidentified wand is named by its metal, and the
/// same wand once identified is named by what it does.
/// </summary>
public sealed class ItemNames
{
    private readonly Appearances _appearances;
    private readonly ItemKnowledge _knowledge;

    public ItemNames(Appearances appearances, ItemKnowledge knowledge)
    {
        ArgumentNullException.ThrowIfNull(appearances);
        ArgumentNullException.ThrowIfNull(knowledge);

        _appearances = appearances;
        _knowledge = knowledge;
    }

    private static bool IsVowel(char letter) =>
        letter is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'I' or 'O' or 'U';

    /// <summary>
    /// Describes an item. Mirrors objdes().
    /// </summary>
    /// <param name="withArticle">
    /// Whether to put an article in front, and with it the whole tail of
    /// enchantment, armour class and inscriptions. Without it the bare name is
    /// produced, which is what the shops and the spell lists want.
    /// </param>
    public string Describe(InvenType item, bool withArticle)
    {
        ArgumentNullException.ThrowIfNull(item);

        int appearance = item.SubVal & (ItemCategory.SingleStackMin - 1);
        string baseName = GameTables.ObjectList[item.Index].Name;
        string? modifier = null;
        string damage = string.Empty;
        var meaning = P1Meaning.Ignored;
        bool unknown = !_knowledge.IsKindKnown(item);
        bool appendName = false;

        switch (item.TVal)
        {
            case ItemCategory.Misc:
            case ItemCategory.Chest:
                break;

            case ItemCategory.SlingAmmo:
            case ItemCategory.Bolt:
            case ItemCategory.Arrow:
                damage = Dice(item);
                break;

            case ItemCategory.Light:
                meaning = P1Meaning.Light;
                break;

            case ItemCategory.Spike:
                break;

            case ItemCategory.Bow:
                // The multiplier is not stored, only implied by which bow it is.
                int multiplier = item.P1 switch
                {
                    1 or 2 => 2,
                    3 or 5 => 3,
                    4 or 6 => 4,
                    _ => -1,
                };

                damage = " (x" + multiplier.ToString(CultureInfo.InvariantCulture) + ")";
                break;

            case ItemCategory.Hafted:
            case ItemCategory.Polearm:
            case ItemCategory.Sword:
                damage = Dice(item);
                meaning = P1Meaning.Flags;
                break;

            case ItemCategory.Digging:
                meaning = P1Meaning.AlwaysPlusses;
                damage = Dice(item);
                break;

            case ItemCategory.Boots:
            case ItemCategory.Gloves:
            case ItemCategory.Cloak:
            case ItemCategory.Helm:
            case ItemCategory.Shield:
            case ItemCategory.HardArmor:
            case ItemCategory.SoftArmor:
                break;

            case ItemCategory.Amulet:
                if (unknown)
                {
                    baseName = "& %s Amulet";
                    modifier = _appearances.Amulets[appearance];
                }
                else
                {
                    baseName = "& Amulet";
                    appendName = true;
                }

                meaning = P1Meaning.Plusses;
                break;

            case ItemCategory.Ring:
                if (unknown)
                {
                    baseName = "& %s Ring";
                    modifier = _appearances.Rocks[appearance];
                }
                else
                {
                    baseName = "& Ring";
                    appendName = true;
                }

                meaning = P1Meaning.Plusses;
                break;

            case ItemCategory.Staff:
                if (unknown)
                {
                    baseName = "& %s Staff";
                    modifier = _appearances.Woods[appearance];
                }
                else
                {
                    baseName = "& Staff";
                    appendName = true;
                }

                meaning = P1Meaning.Charges;
                break;

            case ItemCategory.Wand:
                if (unknown)
                {
                    baseName = "& %s Wand";
                    modifier = _appearances.Metals[appearance];
                }
                else
                {
                    baseName = "& Wand";
                    appendName = true;
                }

                meaning = P1Meaning.Charges;
                break;

            case ItemCategory.Scroll1:
            case ItemCategory.Scroll2:
                if (unknown)
                {
                    baseName = "& Scroll~ titled \"%s\"";
                    modifier = _appearances.Titles[appearance];
                }
                else
                {
                    baseName = "& Scroll~";
                    appendName = true;
                }

                break;

            case ItemCategory.Potion1:
            case ItemCategory.Potion2:
                if (unknown)
                {
                    baseName = "& %s Potion~";
                    modifier = _appearances.Colors[appearance];
                }
                else
                {
                    baseName = "& Potion~";
                    appendName = true;
                }

                break;

            case ItemCategory.Flask:
                break;

            case ItemCategory.Food:
                if (unknown)
                {
                    if (appearance <= 15)
                    {
                        baseName = "& %s Mushroom~";
                    }
                    else if (appearance <= 20)
                    {
                        baseName = "& Hairy %s Mold~";
                    }

                    if (appearance <= 20)
                    {
                        modifier = _appearances.Mushrooms[appearance];
                    }
                }
                else
                {
                    appendName = true;

                    if (appearance <= 15)
                    {
                        baseName = "& Mushroom~";
                    }
                    else if (appearance <= 20)
                    {
                        baseName = "& Hairy Mold~";
                    }
                    else
                    {
                        // Ordinary food is named outright, with nothing appended.
                        appendName = false;
                    }
                }

                break;

            case ItemCategory.MagicBook:
                modifier = baseName;
                baseName = "& Book~ of Magic Spells %s";
                break;

            case ItemCategory.PrayerBook:
                modifier = baseName;
                baseName = "& Holy Book~ of Prayers %s";
                break;

            case ItemCategory.OpenDoor:
            case ItemCategory.ClosedDoor:
            case ItemCategory.SecretDoor:
            case ItemCategory.Rubble:
                break;

            case ItemCategory.Gold:
            case ItemCategory.InvisibleTrap:
            case ItemCategory.VisibleTrap:
            case ItemCategory.UpStair:
            case ItemCategory.DownStair:
                // Terrain, in effect: the table name and nothing else.
                return GameTables.ObjectList[item.Index].Name + ".";

            case ItemCategory.StoreDoor:
                return "the entrance to the "
                    + GameTables.ObjectList[item.Index].Name + ".";

            default:
                return "Error in objdes()";
        }

        var name = new StringBuilder(
            modifier is null ? baseName : Format(baseName, modifier));

        if (appendName)
        {
            name.Append(" of ").Append(GameTables.ObjectList[item.Index].Name);
        }

        // "~" marks where a plural s belongs, and "ch~" the awkward "ches".
        if (item.Number != 1)
        {
            Replace(name, "ch~", "ches");
            Replace(name, "~", "s");
        }
        else
        {
            Replace(name, "~", string.Empty);
        }

        if (!withArticle)
        {
            string bare = name.ToString();

            if (bare.StartsWith("some", StringComparison.Ordinal))
            {
                return bare[5..];
            }

            // The ampersand marks where the article goes; without one, drop it.
            return bare.StartsWith('&') ? bare[2..] : bare;
        }

        AppendEnchantment(name, item, damage, meaning);
        return WithArticle(name.ToString(), item) + Inscription(item);
    }

    private static string Dice(InvenType item) =>
        " (" + item.DamageDice.ToString(CultureInfo.InvariantCulture)
        + "d" + item.DamageSides.ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>Fills the one "%s" a base name may carry.</summary>
    private static string Format(string pattern, string modifier)
    {
        int at = pattern.IndexOf("%s", StringComparison.Ordinal);
        return at < 0 ? pattern : pattern[..at] + modifier + pattern[(at + 2)..];
    }

    private static void Replace(StringBuilder text, string what, string with)
    {
        string current = text.ToString();
        int at = current.IndexOf(what, StringComparison.Ordinal);
        if (at < 0)
        {
            return;
        }

        text.Clear();
        text.Append(current[..at]).Append(with).Append(current[(at + what.Length)..]);
    }

    /// <summary>
    /// Adds everything the player has worked out about this particular item: its
    /// special name, its dice, its bonuses, its armour and its charges.
    /// </summary>
    private static void AppendEnchantment(
        StringBuilder name, InvenType item, string damage, P1Meaning meaning)
    {
        if (item.SpecialName != SpecialName.None && ItemKnowledge.IsEnchantmentKnown(item))
        {
            name.Append(' ').Append(GameTables.SpecialNames[item.SpecialName]);
        }

        if (damage.Length != 0)
        {
            name.Append(damage);
        }

        if (ItemKnowledge.IsEnchantmentKnown(item))
        {
            if ((item.Identification & Identification.ShowHitDam) != 0)
            {
                name.Append(" (").Append(Signed(item.ToHit)).Append(',')
                    .Append(Signed(item.ToDam)).Append(')');
            }
            else if (item.ToHit != 0)
            {
                name.Append(" (").Append(Signed(item.ToHit)).Append(')');
            }
            else if (item.ToDam != 0)
            {
                name.Append(" (").Append(Signed(item.ToDam)).Append(')');
            }
        }

        // Crowns have a base armour class of nothing, so helms are asked about
        // by name rather than by the number being non-zero.
        if (item.Ac != 0 || item.TVal == ItemCategory.Helm)
        {
            name.Append(" [").Append(item.Ac.ToString(CultureInfo.InvariantCulture));

            if (ItemKnowledge.IsEnchantmentKnown(item))
            {
                name.Append(',').Append(Signed(item.ToAc));
            }

            name.Append(']');
        }
        else if (item.ToAc != 0 && ItemKnowledge.IsEnchantmentKnown(item))
        {
            name.Append(" [").Append(Signed(item.ToAc)).Append(']');
        }

        // An item can override what its p1 means, which is how a wand of nothing
        // in particular avoids claiming to have charges.
        if ((item.Identification & Identification.NoShowP1) != 0)
        {
            meaning = P1Meaning.Ignored;
        }
        else if ((item.Identification & Identification.ShowP1) != 0)
        {
            meaning = P1Meaning.AlwaysPlusses;
        }

        if (meaning == P1Meaning.Light)
        {
            name.Append(" with ").Append(item.P1.ToString(CultureInfo.InvariantCulture))
                .Append(" turns of light");
        }
        else if (meaning != P1Meaning.Ignored && ItemKnowledge.IsEnchantmentKnown(item))
        {
            if (meaning == P1Meaning.AlwaysPlusses)
            {
                name.Append(" (").Append(Signed(item.P1)).Append(')');
            }
            else if (meaning == P1Meaning.Charges)
            {
                name.Append(" (").Append(item.P1.ToString(CultureInfo.InvariantCulture))
                    .Append(" charges)");
            }
            else if (item.P1 != 0)
            {
                if (meaning == P1Meaning.Plusses)
                {
                    name.Append(" (").Append(Signed(item.P1)).Append(')');
                }
                else if (meaning == P1Meaning.Flags)
                {
                    if ((item.Flags & ItemFlags.Strength) != 0)
                    {
                        name.Append(" (").Append(Signed(item.P1)).Append(" to STR)");
                    }
                    else if ((item.Flags & ItemFlags.Stealth) != 0)
                    {
                        name.Append(" (").Append(Signed(item.P1)).Append(" to stealth)");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The original wrote these by hand rather than with "%+d", which several of
    /// its machines did not support.
    /// </summary>
    private static string Signed(int value) =>
        (value < 0 ? "-" : "+") + Math.Abs(value).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Puts the article on the front. The count replaces it when there is more
    /// than one, and "no more" when the pile has just been emptied.
    /// </summary>
    private static string WithArticle(string name, InvenType item)
    {
        if (name.StartsWith('&'))
        {
            string tail = name[1..];

            if (item.Number > 1)
            {
                return item.Number.ToString(CultureInfo.InvariantCulture) + tail;
            }

            if (item.Number < 1)
            {
                return "no more" + tail;
            }

            return (IsVowel(tail[1]) ? "an" : "a") + tail;
        }

        if (item.Number < 1)
        {
            return name.StartsWith("some", StringComparison.Ordinal)
                ? "no more " + name[5..]
                : "no more " + name;
        }

        return name;
    }

    /// <summary>
    /// The braces at the end: what the player has guessed, and whatever they
    /// wrote on it themselves.
    /// </summary>
    private string Inscription(InvenType item)
    {
        var marks = new StringBuilder();

        // A shop's stock is never marked as tried, whoever tried one before.
        if (_knowledge.WasTried(item) && !ItemKnowledge.IsStoreBought(item))
        {
            marks.Append("tried ");
        }

        if ((item.Identification
             & (Identification.Magik | Identification.Empty | Identification.Damned)) != 0)
        {
            if ((item.Identification & Identification.Magik) != 0)
            {
                marks.Append("magik ");
            }

            if ((item.Identification & Identification.Empty) != 0)
            {
                marks.Append("empty ");
            }

            if ((item.Identification & Identification.Damned) != 0)
            {
                marks.Append("damned ");
            }
        }

        if (item.Inscription.Length != 0)
        {
            marks.Append(item.Inscription);
        }
        else if (marks.Length > 0)
        {
            // Trim the blank the last mark left behind.
            marks.Length--;
        }

        return marks.Length == 0 ? "." : " {" + marks + "}.";
    }
}
