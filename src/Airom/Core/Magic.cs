// Ported from Umoria 5.6 source/magic.c and source/prayer.c, together with the
// spell bookkeeping in source/misc3.c (spell_chance, print_spells, get_spell,
// calc_spells, gain_spells, calc_mana) and cast_spell from source/moria3.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The player's own magic: casting a mage's spells, reciting a priest's
/// prayers, and the bookkeeping that decides which of them are known.
///
/// A class knows thirty-one spells, numbered the same for everyone but meaning
/// something different for each - see <see cref="GameTables.MagicSpell"/>. The
/// spells a character has learned, has forgotten, and has already got working
/// are three bit sets on the player, one bit per spell.
///
/// A book carries a bit set of the spells printed in it, so casting from a book
/// is the intersection of what the book holds, what the character knows, and
/// what their level allows.
/// </summary>
public class Magic
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Magic(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    private Player Player => _game.Player;

    private Rng Rng => _game.Rng;

    private Spells Spells => _loop.Spells;

    private Inventory Pack => _game.Inventory;

    /// <summary>What an unused entry in the learning order holds.</summary>
    private const byte NoSpell = Airom.Core.Player.NoSpell;

    /// <summary>How many spells there are in a class's list.</summary>
    public const int SpellCount = 31;

    /// <summary>
    /// Where a class's spell names begin in <see cref="GameTables.SpellNames"/>.
    /// Mirrors SPELL_OFFSET and PRAYER_OFFSET.
    /// </summary>
    public const int SpellNameOffset = 0;

    /// <summary>The priest's half of the name table.</summary>
    public const int PrayerNameOffset = 31;

    /// <summary>
    /// The class's own row of the spell table.
    ///
    /// FAITHFUL QUIRK: a warrior has no row, and the original indexes the
    /// table at minus one to find it. That reads whatever happens to sit in
    /// front of the array, and gets away with it because a warrior has no
    /// spells to learn and the loops that would read the row never run. An
    /// empty row is the same thing said safely.
    /// </summary>
    private SpellType[] Book =>
        GameTables.Classes[Player.Class].SpellRealm == SpellRealm.None
            ? []
            : GameTables.MagicSpell[Player.Class - 1];

    private bool IsMage =>
        GameTables.Classes[Player.Class].SpellRealm == SpellRealm.Mage;

    private int NameOffset => IsMage ? SpellNameOffset : PrayerNameOffset;

    /// <summary>What a spell is called, for this class.</summary>
    public string SpellName(int spell) => GameTables.SpellNames[spell + NameOffset];

    /// <summary>
    /// Asks which book to use. Mirrors the get_item() call.
    ///
    /// Returning nothing means the player backed out, which costs no turn. Left
    /// overridable so a harness can answer without a terminal.
    /// </summary>
    protected internal virtual int? ChooseBook(string prompt, int first, int last) =>
        _loop.InventoryScreen.GetItem(prompt, first, last);

    // -------------------------------------------------------------- casting

    /// <summary>
    /// Casts a mage's spell. Mirrors cast().
    ///
    /// Everything that could stop it is checked in order first: sight, light,
    /// a clear head, the right class, and a book to read from. Only then is a
    /// spell chosen, and only then does the turn stop being free.
    /// </summary>
    public void Cast()
    {
        _loop.FreeTurn = true;

        if (Player.Blind > 0)
        {
            _display.MessagePrint("You can't see to read your spell book!");
            return;
        }

        if (_loop.Lighting.NoLight())
        {
            _display.MessagePrint("You have no light to read by.");
            return;
        }

        if (Player.Confused > 0)
        {
            _display.MessagePrint("You are too confused.");
            return;
        }

        if (GameTables.Classes[Player.Class].SpellRealm != SpellRealm.Mage)
        {
            _display.MessagePrint("You can't cast spells!");
            return;
        }

        if (!Pack.FindRange(ItemCategory.MagicBook, ItemCategory.Never,
                            out int first, out int last))
        {
            _display.MessagePrint("But you are not carrying any spell-books!");
            return;
        }

        if (ChooseBook("Use which spell-book?", first, last) is not int slot)
        {
            return;
        }

        int result = CastSpell("Cast which spell?", slot, out int choice, out int chance);

        if (result < 0)
        {
            _display.MessagePrint("You don't know any spells in that book.");
            return;
        }

        if (result == 0)
        {
            return;
        }

        SpellType spell = Book[choice];
        _loop.FreeTurn = false;

        if (Rng.RandInt(100) < chance)
        {
            _display.MessagePrint("You failed to get the spell off!");
        }
        else
        {
            CastMageSpell(choice);
            AwardFirstCasting(choice, spell);
        }

        SpendMana(spell, "You faint from the effort!");
    }

    /// <summary>
    /// Recites a priest's prayer. Mirrors pray().
    ///
    /// The same shape as <see cref="Cast"/>, with two differences the original
    /// has and this keeps: a priest is told to pray harder rather than that
    /// they cannot pray, and an empty pack is complained about before the books
    /// are looked for.
    /// </summary>
    public void Pray()
    {
        _loop.FreeTurn = true;

        if (Player.Blind > 0)
        {
            _display.MessagePrint("You can't see to read your prayer!");
            return;
        }

        if (_loop.Lighting.NoLight())
        {
            _display.MessagePrint("You have no light to read by.");
            return;
        }

        if (Player.Confused > 0)
        {
            _display.MessagePrint("You are too confused.");
            return;
        }

        if (GameTables.Classes[Player.Class].SpellRealm != SpellRealm.Priest)
        {
            _display.MessagePrint("Pray hard enough and your prayers may be answered.");
            return;
        }

        if (Pack.Count == 0)
        {
            _display.MessagePrint("But you are not carrying anything!");
            return;
        }

        if (!Pack.FindRange(ItemCategory.PrayerBook, ItemCategory.Never,
                            out int first, out int last))
        {
            _display.MessagePrint("You are not carrying any Holy Books!");
            return;
        }

        if (ChooseBook("Use which Holy Book?", first, last) is not int slot)
        {
            return;
        }

        int result = CastSpell("Recite which prayer?", slot, out int choice, out int chance);

        if (result < 0)
        {
            _display.MessagePrint("You don't know any prayers in that book.");
            return;
        }

        if (result == 0)
        {
            return;
        }

        SpellType prayer = Book[choice];
        _loop.FreeTurn = false;

        if (Rng.RandInt(100) < chance)
        {
            _display.MessagePrint("You lost your concentration!");
        }
        else
        {
            RecitePrayer(choice);
            AwardFirstCasting(choice, prayer);
        }

        SpendMana(prayer, "You faint from fatigue!");
    }

    /// <summary>
    /// The experience for getting a spell to work for the first time, which is
    /// four times what the table holds.
    ///
    /// Nothing is awarded if the turn stayed free - which happens when the
    /// spell asked for a direction and the player pressed escape, so a spell
    /// backed out of halfway costs neither mana nor a turn.
    /// </summary>
    private void AwardFirstCasting(int choice, SpellType spell)
    {
        if (_loop.FreeTurn || (Player.SpellWorked & (1u << choice)) != 0)
        {
            return;
        }

        Player.Experience += spell.Experience << 2;
        Player.SpellWorked |= 1u << choice;
        _loop.Levelling.PrintExperience();
    }

    /// <summary>
    /// Pays for the spell. Casting more than can be afforded is allowed, and is
    /// paid for with consciousness and sometimes with health.
    /// </summary>
    private void SpendMana(SpellType spell, string faintMessage)
    {
        if (_loop.FreeTurn)
        {
            return;
        }

        if (spell.Mana > Player.CurrentMana)
        {
            _display.MessagePrint(faintMessage);
            Player.Paralysis = Rng.RandInt(5 * (spell.Mana - Player.CurrentMana));
            Player.CurrentMana = 0;
            Player.ManaFraction = 0;

            if (Rng.RandInt(3) == 1)
            {
                _display.MessagePrint("You have damaged your health!");
                _loop.Stats.Decrease(Stat.Constitution);
            }
        }
        else
        {
            Player.CurrentMana -= spell.Mana;
        }

        _display.PrintCurrentMana(Player);
    }

    /// <summary>The thirty-one mage spells. Mirrors the switch in cast().</summary>
    private void CastMageSpell(int choice)
    {
        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;

        switch (choice + 1)
        {
            case 1:
                if (Aimed(out int missile))
                {
                    Spells.FireBolt(SpellElement.MagicMissile, missile, row, column,
                                    Rng.DamRoll(2, 6), SpellName(0));
                }

                break;

            case 2:
                Spells.DetectMonsters();
                break;

            case 3:
                _loop.Combat.Teleport(10);
                break;

            case 4:
                Spells.LightArea(row, column);
                break;

            case 5:
                Spells.HealPlayer(Rng.DamRoll(4, 4));
                break;

            case 6:
                Spells.DetectSecretDoors();
                Spells.DetectTrap();
                break;

            case 7:
                if (Aimed(out int cloud))
                {
                    Spells.FireBall(SpellElement.PoisonGas, cloud, row, column, 12,
                                    SpellName(6));
                }

                break;

            case 8:
                if (Aimed(out int confuse))
                {
                    Spells.ConfuseMonster(confuse, row, column);
                }

                break;

            case 9:
                if (Aimed(out int lightning))
                {
                    Spells.FireBolt(SpellElement.Lightning, lightning, row, column,
                                    Rng.DamRoll(4, 8), SpellName(8));
                }

                break;

            case 10:
                Spells.DestroyDoorsAndTraps();
                break;

            case 11:
                if (Aimed(out int sleep))
                {
                    Spells.SleepMonster(sleep, row, column);
                }

                break;

            case 12:
                Spells.CurePoison();
                break;

            case 13:
                _loop.Combat.Teleport(Player.Level * 5);
                break;

            case 14:
                // Remove curse, which the mage's version does to everything worn
                // or wielded without saying so and without recalculating.
                for (int slot = Inventory.WieldSlot; slot < Inventory.Size; slot++)
                {
                    Pack[slot].Flags &= ~ItemFlags.Cursed;
                }

                break;

            case 15:
                if (Aimed(out int frost))
                {
                    Spells.FireBolt(SpellElement.Frost, frost, row, column,
                                    Rng.DamRoll(6, 8), SpellName(14));
                }

                break;

            case 16:
                if (Aimed(out int mud))
                {
                    Spells.WallToMud(mud, row, column);
                }

                break;

            case 17:
                Spells.CreateFood();
                break;

            case 18:
                Spells.RechargeItem(20);
                break;

            case 19:
                Spells.SleepAdjacent(row, column);
                break;

            case 20:
                if (Aimed(out int polymorph))
                {
                    Spells.PolymorphMonster(polymorph, row, column);
                }

                break;

            case 21:
                Spells.IdentSpell();
                break;

            case 22:
                Spells.SleepEveryMonster();
                break;

            case 23:
                if (Aimed(out int fire))
                {
                    Spells.FireBolt(SpellElement.Fire, fire, row, column,
                                    Rng.DamRoll(9, 8), SpellName(22));
                }

                break;

            case 24:
                if (Aimed(out int slow))
                {
                    Spells.SpeedMonster(slow, row, column, -1);
                }

                break;

            case 25:
                if (Aimed(out int frostBall))
                {
                    Spells.FireBall(SpellElement.Frost, frostBall, row, column, 48,
                                    SpellName(24));
                }

                break;

            case 26:
                Spells.RechargeItem(60);
                break;

            case 27:
                if (Aimed(out int away))
                {
                    Spells.TeleportMonster(away, row, column);
                }

                break;

            case 28:
                Player.Hasted += Rng.RandInt(20) + Player.Level;
                break;

            case 29:
                if (Aimed(out int fireBall))
                {
                    Spells.FireBall(SpellElement.Fire, fireBall, row, column, 72,
                                    SpellName(28));
                }

                break;

            case 30:
                Spells.DestroyArea(row, column);
                break;

            case 31:
                Spells.GenocideSpell();
                break;

            default:
                break;
        }
    }

    /// <summary>The thirty-one prayers. Mirrors the switch in pray().</summary>
    private void RecitePrayer(int choice)
    {
        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;

        switch (choice + 1)
        {
            case 1:
                Spells.DetectEvil();
                break;

            case 2:
                Spells.HealPlayer(Rng.DamRoll(3, 3));
                break;

            case 3:
                Spells.Bless(Rng.RandInt(12) + 12);
                break;

            case 4:
                Spells.RemoveFear();
                break;

            case 5:
                Spells.LightArea(row, column);
                break;

            case 6:
                Spells.DetectTrap();
                break;

            case 7:
                Spells.DetectSecretDoors();
                break;

            case 8:
                Spells.SlowPoison();
                break;

            case 9:
                if (Aimed(out int confuse))
                {
                    Spells.ConfuseMonster(confuse, row, column);
                }

                break;

            case 10:
                _loop.Combat.Teleport(Player.Level * 3);
                break;

            case 11:
                Spells.HealPlayer(Rng.DamRoll(4, 4));
                break;

            case 12:
                Spells.Bless(Rng.RandInt(24) + 24);
                break;

            case 13:
                Spells.SleepAdjacent(row, column);
                break;

            case 14:
                Spells.CreateFood();
                break;

            case 15:
                // FAITHFUL QUIRK: the priest's remove curse walks the whole
                // inventory array rather than the worn slots, and leans on the
                // category test to skip the pack. A wearable thing carried but
                // not worn is uncursed all the same.
                for (int slot = 0; slot < Inventory.Size; slot++)
                {
                    InvenType item = Pack[slot];

                    if (item.TVal >= ItemCategory.MinWear
                        && item.TVal <= ItemCategory.MaxWear)
                    {
                        item.Flags &= ~ItemFlags.Cursed;
                    }
                }

                break;

            case 16:
                Player.ResistHeat += Rng.RandInt(10) + 10;
                Player.ResistCold += Rng.RandInt(10) + 10;
                break;

            case 17:
                Spells.CurePoison();
                break;

            case 18:
                if (Aimed(out int orb))
                {
                    Spells.FireBall(SpellElement.HolyOrb, orb, row, column,
                                    Rng.DamRoll(3, 6) + Player.Level, "Black Sphere");
                }

                break;

            case 19:
                Spells.HealPlayer(Rng.DamRoll(8, 4));
                break;

            case 20:
                Spells.DetectInvisibleFor(Rng.RandInt(24) + 24);
                break;

            case 21:
                Spells.ProtectFromEvil();
                break;

            case 22:
                Spells.Earthquake();
                break;

            case 23:
                Spells.MapArea();
                break;

            case 24:
                Spells.HealPlayer(Rng.DamRoll(16, 4));
                break;

            case 25:
                Spells.TurnUndead();
                break;

            case 26:
                Spells.Bless(Rng.RandInt(48) + 48);
                break;

            case 27:
                Spells.DispelCreature(CreatureDefense.Undead, 3 * Player.Level);
                break;

            case 28:
                Spells.HealPlayer(200);
                break;

            case 29:
                Spells.DispelCreature(CreatureDefense.Evil, 3 * Player.Level);
                break;

            case 30:
                Spells.WardingGlyph();
                break;

            case 31:
                // Holy Invulnerability, which is every good thing at once.
                Spells.RemoveFear();
                Spells.CurePoison();
                Spells.HealPlayer(1000);

                for (int stat = Stat.Strength; stat <= Stat.Charisma; stat++)
                {
                    _loop.Stats.Restore(stat);
                }

                Spells.DispelCreature(CreatureDefense.Evil, 4 * Player.Level);
                Spells.TurnUndead();

                Player.Invulnerable = Player.Invulnerable < 3
                    ? 3
                    : Player.Invulnerable + 1;
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Asks which way the spell goes. Backing out leaves the turn free, which
    /// is what makes a cancelled spell cost nothing at all.
    /// </summary>
    private bool Aimed(out int direction)
    {
        (bool taken, int chosen) = _loop.ReadDirection();
        direction = chosen;
        return taken;
    }

    // ------------------------------------------------------------- choosing

    /// <summary>
    /// Works out which spells in a book can be cast, and asks for one. Mirrors
    /// cast_spell().
    ///
    /// A book's flags are the spells printed in it. The first bit set, whether
    /// known or not, fixes the lettering: a spell keeps the same letter in the
    /// same book however few of them the character has learned.
    /// </summary>
    /// <returns>
    /// -1 when nothing in the book can be cast, 0 when the player backed out,
    /// and 1 when a spell was chosen.
    /// </returns>
    public int CastSpell(string prompt, int slot, out int choice, out int chance)
    {
        choice = -1;
        chance = 0;

        uint printed = Pack[slot].Flags;
        int firstSpell = BitPos(ref printed);

        uint available = Pack[slot].Flags & Player.SpellLearned;
        var castable = new int[SpellCount];
        int count = 0;

        while (available != 0)
        {
            int spell = BitPos(ref available);

            if (Book[spell].Level <= Player.Level)
            {
                castable[count] = spell;
                count++;
            }
        }

        if (count == 0)
        {
            return -1;
        }

        int result = GetSpell(castable, count, out choice, out chance, prompt, firstSpell)
            ? 1
            : 0;

        if (result != 0 && Book[choice].Mana > Player.CurrentMana)
        {
            bool confirmed = _display.GetCheck(
                IsMage
                    ? "You summon your limited strength to cast this one! Confirm?"
                    : "The gods may think you presumptuous for this! Confirm?");

            result = confirmed ? 1 : 0;
        }

        return result;
    }

    /// <summary>
    /// Asks which of the listed spells to cast. Mirrors get_spell().
    ///
    /// A capital letter asks for confirmation and a small one does not, which
    /// is how an expensive spell can be cast without a second thought once the
    /// player is sure of it. A star lists the spells, once.
    /// </summary>
    public bool GetSpell(
        int[] spells, int count, out int number, out int chance,
        string prompt, int firstSpell)
    {
        ArgumentNullException.ThrowIfNull(spells);

        number = -1;
        chance = 0;

        bool chosen = false;
        bool redrawn = false;

        string header = "(Spells "
            + (char)(spells[0] + 'a' - firstSpell) + "-"
            + (char)(spells[count - 1] + 'a' - firstSpell)
            + ", *=List, <ESCAPE>=exit) " + prompt;

        while (!chosen && _display.GetCommand(header, out char choice))
        {
            if (char.IsUpper(choice))
            {
                number = choice - 'A' + firstSpell;

                if (!Listed(spells, count, number))
                {
                    number = -2;
                }
                else
                {
                    SpellType spell = Book[number];

                    bool confirmed = _display.GetCheck(
                        "Cast " + SpellName(number) + " ("
                        + spell.Mana.ToString(CultureInfo.InvariantCulture) + " mana, "
                        + SpellChance(number).ToString(CultureInfo.InvariantCulture)
                        + "% fail)?");

                    if (confirmed)
                    {
                        chosen = true;
                    }
                    else
                    {
                        number = -1;
                    }
                }
            }
            else if (char.IsLower(choice))
            {
                number = choice - 'a' + firstSpell;

                if (!Listed(spells, count, number))
                {
                    number = -2;
                }
                else
                {
                    chosen = true;
                }
            }
            else if (choice == '*')
            {
                // Only ever drawn once, so a second star costs nothing.
                if (!redrawn)
                {
                    _display.SaveScreen();
                    redrawn = true;
                    PrintSpells(spells, count, comment: false, firstSpell);
                }
            }
            else if (char.IsLetter(choice))
            {
                number = -2;
            }
            else
            {
                number = -1;
                _display.Bell();
            }

            if (number == -2)
            {
                _display.MessagePrint(
                    "You don't know that " + (IsMage ? "spell" : "prayer") + ".");
            }
        }

        if (redrawn)
        {
            _display.RestoreScreen();
        }

        _display.EraseLine(Display.MessageLine, 0);

        if (chosen)
        {
            chance = SpellChance(number);
        }

        return chosen;
    }

    /// <summary>Whether a spell number is one of the ones on offer.</summary>
    private static bool Listed(int[] spells, int count, int number)
    {
        for (int i = 0; i < count; i++)
        {
            if (spells[i] == number)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// How likely a spell is to fail, as a percentage. Mirrors spell_chance().
    ///
    /// Levels above the spell's own make it easier, a good casting stat makes it
    /// easier still, and reaching for mana that is not there makes it much
    /// harder. Never quite certain, and never quite hopeless: five to ninety-five.
    /// </summary>
    public int SpellChance(int spell)
    {
        SpellType entry = Book[spell];

        int chance = entry.Fail - (3 * (Player.Level - entry.Level));
        int stat = IsMage ? Stat.Intelligence : Stat.Wisdom;

        chance -= 3 * (Stats.Adjustment(Player, stat) - 1);

        if (entry.Mana > Player.CurrentMana)
        {
            chance += 5 * (entry.Mana - Player.CurrentMana);
        }

        return Math.Clamp(chance, 5, 95);
    }

    /// <summary>
    /// Lists spells down the right of the screen. Mirrors print_spells().
    ///
    /// Only the first twenty-two fit, so that is all that is shown - and all
    /// that can be picked, which the learning prompt relies on.
    /// </summary>
    /// <param name="comment">
    /// Whether to say what is known of each spell, which the browse command
    /// wants and the casting prompt does not. It also shifts the list left, to
    /// make room for the note.
    /// </param>
    /// <param name="nonConsecutive">
    /// -1 to letter the list straight through from 'a', which is what learning
    /// wants; otherwise the spell number that 'a' stands for, so that the
    /// letters match the book and leave gaps for the spells not on offer.
    /// </param>
    public void PrintSpells(int[] spells, int count, bool comment, int nonConsecutive)
    {
        ArgumentNullException.ThrowIfNull(spells);

        int column = comment ? 22 : 31;

        _display.EraseLine(1, column);
        _display.PutBuffer("Name", 1, column + 5);
        _display.PutBuffer("Lv Mana Fail", 1, column + 35);

        if (count > 22)
        {
            count = 22;
        }

        for (int i = 0; i < count; i++)
        {
            int spell = spells[i];
            SpellType entry = Book[spell];

            string note = !comment ? string.Empty
                : (Player.SpellForgotten & (1u << spell)) != 0 ? " forgotten"
                : (Player.SpellLearned & (1u << spell)) == 0 ? " unknown"
                : (Player.SpellWorked & (1u << spell)) == 0 ? " untried"
                : string.Empty;

            char letter = nonConsecutive == -1
                ? (char)('a' + i)
                : (char)('a' + spell - nonConsecutive);

            string line = "  " + letter + ") "
                + SpellName(spell).PadRight(30)
                + Pad(entry.Level, 2) + " "
                + Pad(entry.Mana, 4) + " "
                + Pad(SpellChance(spell), 3) + "%" + note;

            _display.Print(line, 2 + i, column);
        }
    }

    private static string Pad(int value, int width) =>
        value.ToString(CultureInfo.InvariantCulture).PadLeft(width);

    /// <summary>
    /// Takes the lowest set bit out of a mask and returns which one it was.
    /// Mirrors bit_pos(). Returns -1 when there is nothing left.
    /// </summary>
    private static int BitPos(ref uint mask)
    {
        if (mask == 0)
        {
            return -1;
        }

        int bit = System.Numerics.BitOperations.TrailingZeroCount(mask);
        mask &= ~(1u << bit);
        return bit;
    }

    /// <summary>
    /// Looks a book over without casting from it. Mirrors examine_book().
    ///
    /// Everything printed in the book is listed, with a note beside each saying
    /// whether it is known, forgotten, untried or nothing in particular. A book
    /// of the wrong kind is unreadable whatever the character's level.
    /// </summary>
    public void ExamineBook()
    {
        if (!Pack.FindRange(ItemCategory.MagicBook, ItemCategory.PrayerBook,
                            out int first, out int last))
        {
            _display.MessagePrint("You are not carrying any books.");
            return;
        }

        if (Player.Blind > 0)
        {
            _display.MessagePrint("You can't see to read your spell book!");
            return;
        }

        if (_loop.Lighting.NoLight())
        {
            _display.MessagePrint("You have no light to read by.");
            return;
        }

        if (Player.Confused > 0)
        {
            _display.MessagePrint("You are too confused.");
            return;
        }

        if (ChooseBook("Which Book?", first, last) is not int slot)
        {
            return;
        }

        int realm = GameTables.Classes[Player.Class].SpellRealm;
        byte wanted = realm == SpellRealm.Mage ? ItemCategory.MagicBook
            : realm == SpellRealm.Priest ? ItemCategory.PrayerBook
            : ItemCategory.Nothing;

        if (wanted == ItemCategory.Nothing || Pack[slot].TVal != wanted)
        {
            _display.MessagePrint("You do not understand the language.");
            return;
        }

        // Everything the book holds that this class has any use for, known or
        // not - a spell it can never learn is marked with a level of 99.
        uint printed = Pack[slot].Flags;
        var listed = new int[SpellCount];
        int count = 0;

        while (printed != 0)
        {
            int spell = BitPos(ref printed);

            if (spell < SpellCount && Book[spell].Level < 99)
            {
                listed[count] = spell;
                count++;
            }
        }

        _display.SaveScreen();
        PrintSpells(listed, count, comment: true, -1);
        _display.PauseLine(0);
        _display.RestoreScreen();
    }

    // ---------------------------------------------------------- bookkeeping

    /// <summary>
    /// Works out how many spells the character should know, and forgets or
    /// remembers until that is the number they know. Mirrors calc_spells().
    ///
    /// Spells are forgotten newest first and remembered oldest first, which is
    /// what <see cref="Player.SpellOrder"/> records. A forgotten spell is never
    /// lost outright: raising the stat or the level again brings it back.
    /// </summary>
    public void CalcSpells(int stat)
    {
        SpellType[] book = Book;
        string kind = stat == Stat.Intelligence ? "spell" : "prayer";
        int offset = stat == Stat.Intelligence ? SpellNameOffset : PrayerNameOffset;

        // Anything known that the character has outgrown downwards - a drained
        // level - goes first. The walk is from the top down and stops at the
        // first spell that is still within reach, since the table is ordered by
        // level.
        for (int i = 31; i >= 0; i--)
        {
            uint mask = 1u << i;

            if ((mask & Player.SpellLearned) == 0)
            {
                continue;
            }

            if (i < SpellCount && book[i].Level > Player.Level)
            {
                Player.SpellLearned &= ~mask;
                Player.SpellForgotten |= mask;
                _display.MessagePrint(
                    "You have forgotten the " + kind + " of "
                    + GameTables.SpellNames[i + offset] + ".");
            }
            else
            {
                break;
            }
        }

        int levels = Player.Level - GameTables.Classes[Player.Class].FirstSpellLevel + 1;

        int allowed = Stats.Adjustment(Player, stat) switch
        {
            0 => 0,
            1 or 2 or 3 => 1 * levels,
            4 or 5 => 3 * levels / 2,
            6 => 2 * levels,
            _ => 5 * levels / 2,
        };

        int known = System.Numerics.BitOperations.PopCount(Player.SpellLearned);
        int newSpells = allowed - known;

        if (newSpells > 0)
        {
            // Remember what was forgotten, oldest first, before offering
            // anything new. A spell still out of reach does not come back, but
            // it does not block the ones behind it either - the allowance is
            // raised so the walk keeps going.
            for (int i = 0;
                 Player.SpellForgotten != 0 && newSpells > 0 && i < allowed && i < 32;
                 i++)
            {
                int spell = Player.SpellOrder[i];

                // Shifting by more than the width of the mask is undefined in
                // the original, so an unused entry is given no bit at all.
                uint mask = spell == NoSpell ? 0u : 1u << spell;

                if ((mask & Player.SpellForgotten) == 0)
                {
                    continue;
                }

                if (book[spell].Level <= Player.Level)
                {
                    newSpells--;
                    Player.SpellForgotten &= ~mask;
                    Player.SpellLearned |= mask;
                    _display.MessagePrint(
                        "You have remembered the " + kind + " of "
                        + GameTables.SpellNames[spell + offset] + ".");
                }
                else
                {
                    allowed++;
                }
            }

            if (newSpells > 0)
            {
                // How many are left to learn at all. Whether the books are
                // carried is not asked here - that is the learning command's
                // business - so this is the ceiling rather than the offer.
                uint learnable = 0x7FFFFFFFu & ~Player.SpellLearned;
                int reachable = 0;

                while (learnable != 0)
                {
                    int spell = BitPos(ref learnable);

                    if (spell < SpellCount && book[spell].Level <= Player.Level)
                    {
                        reachable++;
                    }
                }

                if (newSpells > reachable)
                {
                    newSpells = reachable;
                }
            }
        }
        else if (newSpells < 0)
        {
            // Too many known, so they go newest first - the opposite order to
            // remembering.
            for (int i = 31; i >= 0 && newSpells != 0 && Player.SpellLearned != 0; i--)
            {
                int spell = Player.SpellOrder[i];
                uint mask = spell == NoSpell ? 0u : 1u << spell;

                if ((mask & Player.SpellLearned) == 0)
                {
                    continue;
                }

                Player.SpellLearned &= ~mask;
                Player.SpellForgotten |= mask;
                newSpells++;
                _display.MessagePrint(
                    "You have forgotten the " + kind + " of "
                    + GameTables.SpellNames[spell + offset] + ".");
            }

            newSpells = 0;
        }

        if (newSpells != Player.NewSpells)
        {
            if (newSpells > 0 && Player.NewSpells == 0)
            {
                _display.MessagePrint("You can learn some new " + kind + "s now.");
            }

            Player.NewSpells = newSpells;
            Player.Status |= PlayerStatus.CanStudy;
        }
    }

    /// <summary>
    /// Learns the spells the character has earned. Mirrors gain_spells().
    ///
    /// A mage picks which, and needs the book in hand to do it. A priest is
    /// given one at random and needs no book at all, since the prayer comes
    /// from their god rather than the page.
    /// </summary>
    public void GainSpells()
    {
        if (Player.Confused > 0)
        {
            _display.MessagePrint("You are too confused.");
            return;
        }

        int newSpells = Player.NewSpells;
        int lostToMissingBooks = 0;
        SpellType[] book = Book;

        bool mage = IsMage;
        int stat = mage ? Stat.Intelligence : Stat.Wisdom;
        int offset = mage ? SpellNameOffset : PrayerNameOffset;

        if (mage)
        {
            if (Player.Blind > 0)
            {
                _display.MessagePrint("You can't see to read your spell book!");
                return;
            }

            if (_loop.Lighting.NoLight())
            {
                _display.MessagePrint("You have no light to read by.");
                return;
            }
        }

        int lastKnown = 0;

        while (lastKnown < 32 && Player.SpellOrder[lastKnown] != NoSpell)
        {
            lastKnown++;
        }

        if (newSpells == 0)
        {
            _display.MessagePrint(
                "You can't learn any new " + (mage ? "spell" : "prayer") + "s!");
            _loop.FreeTurn = true;
            return;
        }

        // A mage can only learn what is written in a book they are carrying; a
        // priest may learn anything at all.
        uint learnable = 0;

        if (mage)
        {
            for (int i = 0; i < Pack.Count; i++)
            {
                if (Pack[i].TVal == ItemCategory.MagicBook)
                {
                    learnable |= Pack[i].Flags;
                }
            }
        }
        else
        {
            learnable = 0x7FFFFFFFu;
        }

        learnable &= ~Player.SpellLearned;

        var choices = new int[SpellCount];
        int count = 0;

        while (learnable != 0)
        {
            int spell = BitPos(ref learnable);

            if (spell < SpellCount && book[spell].Level <= Player.Level)
            {
                choices[count] = spell;
                count++;
            }
        }

        if (newSpells > count)
        {
            _display.MessagePrint("You seem to be missing a book.");
            lostToMissingBooks = newSpells - count;
            newSpells = count;
        }

        if (newSpells > 0 && mage)
        {
            _display.SaveScreen();
            PrintSpells(choices, count, comment: false, -1);

            while (newSpells > 0 && _display.GetCommand("Learn which spell?", out char key))
            {
                int picked = key - 'a';

                // Only twenty-two are ever shown, so only twenty-two can be
                // picked however many are on offer.
                if (picked >= 0 && picked < count && picked < 22)
                {
                    newSpells--;
                    Player.SpellLearned |= 1u << choices[picked];
                    Player.SpellOrder[lastKnown] = (byte)choices[picked];
                    lastKnown++;

                    for (int j = picked; j <= count - 1; j++)
                    {
                        choices[j] = j + 1 < choices.Length ? choices[j + 1] : 0;
                    }

                    count--;
                    _display.EraseLine(picked + 1, 31);
                    PrintSpells(choices, count, comment: false, -1);
                }
                else
                {
                    _display.Bell();
                }
            }

            _display.RestoreScreen();
        }
        else if (newSpells > 0)
        {
            while (newSpells > 0)
            {
                int picked = Rng.RandInt(count) - 1;

                Player.SpellLearned |= 1u << choices[picked];
                Player.SpellOrder[lastKnown] = (byte)choices[picked];
                lastKnown++;

                _display.MessagePrint(
                    "You have learned the prayer of "
                    + GameTables.SpellNames[choices[picked] + offset] + ".");

                for (int j = picked; j <= count - 1; j++)
                {
                    choices[j] = j + 1 < choices.Length ? choices[j + 1] : 0;
                }

                count--;
                newSpells--;
            }
        }

        Player.NewSpells = newSpells + lostToMissingBooks;

        // FAITHFUL QUIRK: the flag that redraws "Study" is raised only when
        // there is nothing left to learn, so the counter on the screen can lag
        // behind until something else redraws it.
        if (Player.NewSpells == 0)
        {
            Player.Status |= PlayerStatus.CanStudy;
        }

        // First level characters have no mana at all until they know a spell.
        if (Player.MaxMana == 0)
        {
            CalcMana(stat);
        }
    }

    /// <summary>
    /// Works out how much mana the character has. Mirrors calc_mana().
    ///
    /// Nothing at all until the first spell is learned. The current pool is
    /// moved in proportion when the maximum changes, so gaining a level does
    /// not fill an empty caster up.
    /// </summary>
    public void CalcMana(int stat)
    {
        if (Player.SpellLearned == 0)
        {
            if (Player.MaxMana != 0)
            {
                Player.MaxMana = 0;
                Player.CurrentMana = 0;
                Player.Status |= PlayerStatus.ManaChanged;
            }

            return;
        }

        int levels = Player.Level - GameTables.Classes[Player.Class].FirstSpellLevel + 1;

        int mana = Stats.Adjustment(Player, stat) switch
        {
            0 => 0,
            1 or 2 => 1 * levels,
            3 => 3 * levels / 2,
            4 => 2 * levels,
            5 => 5 * levels / 2,
            6 => 3 * levels,
            _ => 4 * levels,
        };

        // One extra, so that a first level caster has two rather than one.
        if (mana > 0)
        {
            mana++;
        }

        if (Player.MaxMana == mana)
        {
            return;
        }

        if (Player.MaxMana != 0)
        {
            // Divided before multiplied, as the original does, to keep the
            // arithmetic inside a long at the cost of a little accuracy.
            long value = ((((long)Player.CurrentMana << 16) + Player.ManaFraction)
                          / Player.MaxMana) * mana;

            Player.CurrentMana = (int)(value >> 16);
            Player.ManaFraction = (int)(value & 0xFFFF);
        }
        else
        {
            Player.CurrentMana = mana;
            Player.ManaFraction = 0;
        }

        Player.MaxMana = mana;

        // Not printed here: this can run inside a shop or the inventory screen,
        // where the sidebar is not on show.
        Player.Status |= PlayerStatus.ManaChanged;
    }
}
