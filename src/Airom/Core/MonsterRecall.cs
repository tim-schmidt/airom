// Ported from Umoria 5.6 source/recall.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using System.Text;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// What the player has learned about a kind of creature, written out as prose.
///
/// Nothing here is looked up: everything said is something the player found out
/// by being hit, by killing one, or by watching. The memory records what has
/// been seen and this turns it into sentences - so two players will read
/// different things about the same creature, and a creature nobody has met
/// says almost nothing at all.
///
/// Some facts need more than one meeting. The armour rating needs kills enough
/// to have judged it, and a creature's own level makes that easier, since a
/// deep one is studied more carefully. The damage an attack does needs to have
/// been felt often enough, and a heavy attack needs to be felt more often - see
/// <see cref="KnowsArmour"/> and <see cref="KnowsDamage"/>.
/// </summary>
public class MonsterRecall
{
    private readonly GameState _game;
    private readonly Display _display;

    public MonsterRecall(GameState game, Display display)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);

        _game = game;
        _display = display;
    }

    private Player Player => _game.Player;

    /// <summary>
    /// The deepest a winning creature is said to be found at, however high its
    /// level. Umoria's WIN_MON_APPEAR: the Balrog is level 100 but walks the
    /// fiftieth floor.
    /// </summary>
    private const int WinningMonsterDepth = 50;

    /// <summary>Umoria's MAX_UCHAR, which a wizard's memory is filled with.</summary>
    private const int MaxByte = 255;

    /// <summary>
    /// Whether anything at all is known about a kind of creature. Mirrors
    /// bool_roff_recall().
    /// </summary>
    public bool KnowsAnything(int creatureIndex)
    {
        if (_game.Wizard)
        {
            return true;
        }

        MonsterMemory memory = _game.Memories[creatureIndex];

        if (memory.Move != 0 || memory.Defense != 0 || memory.Kills != 0
            || memory.Spells != 0 || memory.Deaths != 0)
        {
            return true;
        }

        foreach (byte attack in memory.Attacks)
        {
            if (attack != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether enough have been killed to have judged how hard they are to
    /// hurt. Mirrors knowarmor(): a deeper creature needs fewer kills, since
    /// meeting one at all is an education.
    /// </summary>
    private static bool KnowsArmour(int level, int kills) => kills > 304 / (4 + level);

    /// <summary>
    /// Whether an attack has been felt often enough to know what it does.
    /// Mirrors knowdamage(): the harder it hits, the more often it has to land
    /// before the number is worth trusting.
    /// </summary>
    private static bool KnowsDamage(int level, int landed, int damage) =>
        (4 + level) * landed > 80 * damage;

    /// <summary>
    /// Writes out everything known about a kind of creature, and waits for a
    /// key. Mirrors roff_recall().
    /// </summary>
    /// <returns>
    /// The key that ended it, so escape can abort whatever was showing this.
    /// </returns>
    public char Describe(int creatureIndex)
    {
        MonsterMemory memory = _game.Memories[creatureIndex];
        CreatureType creature = GameTables.CreatureList[creatureIndex];

        MonsterMemory? saved = null;

        if (_game.Wizard)
        {
            // A wizard sees everything: the memory is filled in, read out, and
            // put back exactly as it was.
            saved = FillInForWizard(memory, creature);
        }

        var page = new Page(_display);

        Write(page, memory, creature);

        _display.Print("--pause--", page.Line, 0);

        if (saved is not null)
        {
            memory.CopyFrom(saved);
        }

        return _display.ReadKey();
    }

    /// <summary>
    /// Fills the memory in as though everything had been seen, and hands back
    /// what was there before.
    /// </summary>
    private static MonsterMemory FillInForWizard(
        MonsterMemory memory, CreatureType creature)
    {
        var saved = new MonsterMemory();
        saved.CopyFrom(memory);

        memory.Kills = GameLoop.MaxShort;
        memory.Wake = MaxByte;
        memory.Ignore = MaxByte;

        // The treasure bits are a count in the memory but flags in the table,
        // so they are turned back into a count here.
        uint drops =
            (uint)((((creature.MoveFlags & CreatureMove.Drop4d2Objects) != 0 ? 1 : 0) * 8)
                + (((creature.MoveFlags & CreatureMove.Drop2d2Objects) != 0 ? 1 : 0) * 4)
                + (((creature.MoveFlags & CreatureMove.Drop1d2Objects) != 0 ? 1 : 0) * 2)
                + ((creature.MoveFlags & CreatureMove.Drop90Percent) != 0 ? 1 : 0)
                + ((creature.MoveFlags & CreatureMove.Drop60Percent) != 0 ? 1 : 0));

        memory.Move = (creature.MoveFlags & ~CreatureMove.Treasure)
            | (drops << CreatureMove.TreasureShift);

        memory.Defense = creature.DefenseFlags;

        memory.Spells = (creature.SpellFlags & CreatureSpell.Frequency) != 0
            ? creature.SpellFlags | CreatureSpell.Frequency
            : creature.SpellFlags;

        int i = 0;

        foreach (byte attack in creature.Attacks)
        {
            if (attack == 0 || i >= MonsterMemory.MaxAttacks)
            {
                break;
            }

            memory.Attacks[i] = MaxByte;
            i++;
        }

        // A little hack, so that something which only ever attacks by magic
        // still has its attacks described.
        if ((memory.Move & CreatureMove.OnlyMagic) != 0)
        {
            memory.Attacks[0] = MaxByte;
        }

        return saved;
    }

    /// <summary>
    /// Everything said about a creature, in the order the original says it.
    /// </summary>
    private void Write(Page page, MonsterMemory memory, CreatureType creature)
    {
        uint spells = memory.Spells & creature.SpellFlags & ~CreatureSpell.Frequency;

        // Whether killing it wins the game is never a secret.
        uint move = memory.Move | (CreatureMove.Win & creature.MoveFlags);
        ushort defense = (ushort)(memory.Defense & creature.DefenseFlags);

        page.Write("The " + creature.Name + ":\n");

        WriteHistory(page, memory);
        WriteMovement(page, memory, creature, move);
        WriteWorth(page, memory, creature);
        WriteSpells(page, memory, creature, spells);
        WriteToughness(page, memory, creature);
        WriteAbilities(page, move);
        WriteWeaknesses(page, defense);
        WriteAwareness(page, memory, creature);
        WriteTreasure(page, creature, move);
        WriteAttacks(page, memory, creature);

        if ((creature.MoveFlags & CreatureMove.Win) != 0)
        {
            page.Write(" Killing one of these wins the game!");
        }

        page.Write("\n");
    }

    /// <summary>How the fight has gone so far, for everyone who has had it.</summary>
    private static void WriteHistory(Page page, MonsterMemory memory)
    {
        if (memory.Deaths != 0)
        {
            page.Write(Count(memory.Deaths)
                + " of the contributors to your monster memory "
                + Plural(memory.Deaths, "has", "have"));

            page.Write(" been killed by this creature, and ");

            page.Write(memory.Kills == 0
                ? "it is not ever known to have been defeated."
                : "at least " + Count(memory.Kills) + " of the beasts "
                    + Plural(memory.Kills, "has", "have") + " been exterminated.");

            return;
        }

        if (memory.Kills != 0)
        {
            page.Write("At least " + Count(memory.Kills) + " of these creatures "
                + Plural(memory.Kills, "has", "have"));

            page.Write(" been killed by contributors to your monster memory.");
            return;
        }

        page.Write("No known battles to the death are recalled.");
    }

    /// <summary>
    /// Where it lives and how it gets about - which is obvious on sight, except
    /// for the depth, which needs a kill to have been noticed.
    /// </summary>
    private static void WriteMovement(
        Page page, MonsterMemory memory, CreatureType creature, uint move)
    {
        bool said = false;

        if (creature.Level == 0)
        {
            page.Write(" It lives in the town");
            said = true;
        }
        else if (memory.Kills != 0)
        {
            int depth = Math.Min((int)creature.Level, WinningMonsterDepth);

            page.Write(" It is normally found at depths of "
                + Count(depth * 50) + " feet");

            said = true;
        }

        // The table's speed is ten higher than the real one, so that it fits in
        // a byte.
        int speed = creature.Speed - 10;

        if ((move & CreatureMove.AllMoveFlags) != 0)
        {
            page.Write(said ? ", and" : " It");
            said = true;

            page.Write(" moves");

            if ((move & CreatureMove.RandomMove) != 0)
            {
                page.Write(GameTables.HowErratically[
                    (int)((move & CreatureMove.RandomMove) >> 3)]);

                page.Write(" erratically");
            }

            if (speed == 1)
            {
                page.Write(" at normal speed");
            }
            else
            {
                if ((move & CreatureMove.RandomMove) != 0)
                {
                    page.Write(", and");
                }

                if (speed <= 0)
                {
                    if (speed == -1)
                    {
                        page.Write(" very");
                    }
                    else if (speed < -1)
                    {
                        page.Write(" incredibly");
                    }

                    page.Write(" slowly");
                }
                else
                {
                    if (speed == 3)
                    {
                        page.Write(" very");
                    }
                    else if (speed > 3)
                    {
                        page.Write(" unbelievably");
                    }

                    page.Write(" quickly");
                }
            }
        }

        if ((move & CreatureMove.AttackOnly) != 0)
        {
            page.Write(said ? ", but" : " It");
            said = true;
            page.Write(" does not deign to chase intruders");
        }

        if ((move & CreatureMove.OnlyMagic) != 0)
        {
            page.Write(said ? ", but" : " It");
            said = true;
            page.Write(" always moves and attacks by using magic");
        }

        if (said)
        {
            page.Write(".");
        }
    }

    /// <summary>
    /// What killing one is worth, and what kind of thing it is. One kill
    /// teaches both; being a dragon is obvious without one.
    /// </summary>
    private void WriteWorth(Page page, MonsterMemory memory, CreatureType creature)
    {
        if (memory.Kills == 0)
        {
            return;
        }

        page.Write(" A kill of this");

        if ((creature.DefenseFlags & CreatureDefense.Animal) != 0)
        {
            page.Write(" natural");
        }

        if ((creature.DefenseFlags & CreatureDefense.Evil) != 0)
        {
            page.Write(" evil");
        }

        if ((creature.DefenseFlags & CreatureDefense.Undead) != 0)
        {
            page.Write(" undead");
        }

        // Worked out in long arithmetic: a first level character looking at the
        // Balrog overflows anything smaller.
        long whole = (long)creature.KillExperience * creature.Level / Player.Level;

        long fraction =
            ((((long)creature.KillExperience * creature.Level % Player.Level)
              * 1000 / Player.Level) + 5) / 10;

        page.Write(" creature is worth "
            + whole.ToString(CultureInfo.InvariantCulture) + "."
            + fraction.ToString("00", CultureInfo.InvariantCulture)
            + " point" + (whole == 1 && fraction == 0 ? string.Empty : "s"));

        page.Write(" for a" + AnBefore(Player.Level) + " "
            + Count(Player.Level) + Ordinal(Player.Level) + " level character.");
    }

    /// <summary>
    /// What it breathes and what it casts - and, quietly, what it is proof
    /// against: a creature that has never breathed at the player may still be
    /// known to shrug off the same element.
    /// </summary>
    private static void WriteSpells(
        Page page, MonsterMemory memory, CreatureType creature, uint spells)
    {
        bool first = true;
        uint left = spells;

        for (int i = 0; (left & CreatureSpell.Breathe) != 0; i++)
        {
            if ((left & (CreatureSpell.BreatheLightning << i)) == 0)
            {
                continue;
            }

            left &= ~(CreatureSpell.BreatheLightning << i);

            if (first)
            {
                page.Write((memory.Spells & CreatureSpell.Frequency) != 0
                    ? " It can breathe "
                    : " It is resistant to ");

                first = false;
            }
            else
            {
                page.Write((left & CreatureSpell.Breathe) != 0 ? ", " : " and ");
            }

            page.Write(GameTables.BreathDescriptions[i]);
        }

        first = true;

        for (int i = 0; (left & CreatureSpell.Spells) != 0; i++)
        {
            if ((left & (CreatureSpell.TeleportShort << i)) == 0)
            {
                continue;
            }

            left &= ~(CreatureSpell.TeleportShort << i);

            if (first)
            {
                page.Write((spells & CreatureSpell.Breathe) != 0
                    ? ", and is also"
                    : " It is");

                page.Write(" magical, casting spells which ");
                first = false;
            }
            else
            {
                page.Write((left & CreatureSpell.Spells) != 0 ? ", " : " or ");
            }

            page.Write(GameTables.SpellDescriptions[i]);
        }

        if ((spells & (CreatureSpell.Breathe | CreatureSpell.Spells)) == 0)
        {
            return;
        }

        // How often, once it has been seen enough times to have counted.
        if ((memory.Spells & CreatureSpell.Frequency) > 5)
        {
            page.Write("; 1 time in "
                + Count((int)(creature.SpellFlags & CreatureSpell.Frequency)));
        }

        page.Write(".");
    }

    /// <summary>How hard it is to hurt, once enough have been killed to judge.</summary>
    private static void WriteToughness(
        Page page, MonsterMemory memory, CreatureType creature)
    {
        if (!KnowsArmour(creature.Level, memory.Kills))
        {
            return;
        }

        page.Write(" It has an armor rating of " + Count(creature.Ac));

        page.Write(" and a"
            + ((creature.DefenseFlags & CreatureDefense.MaxHitPoints) != 0
                ? " maximized"
                : string.Empty)
            + " life rating of " + Count(creature.HitDiceCount) + "d"
            + Count(creature.HitDiceSides) + ".");
    }

    /// <summary>What it can do that ordinary creatures cannot.</summary>
    private static void WriteAbilities(Page page, uint move)
    {
        bool first = true;
        uint left = move;

        for (int i = 0; (left & CreatureMove.Special) != 0; i++)
        {
            if ((left & (CreatureMove.Invisible << i)) == 0)
            {
                continue;
            }

            left &= ~(CreatureMove.Invisible << i);

            if (first)
            {
                page.Write(" It can ");
                first = false;
            }
            else
            {
                page.Write((left & CreatureMove.Special) != 0 ? ", " : " and ");
            }

            page.Write(GameTables.SpecialAbilities[i]);
        }

        if (!first)
        {
            page.Write(".");
        }
    }

    /// <summary>What hurts it more than it should, and what does not touch it.</summary>
    private static void WriteWeaknesses(Page page, ushort defense)
    {
        bool first = true;
        int left = defense;

        for (int i = 0; (left & CreatureDefense.Weakness) != 0; i++)
        {
            if ((left & (CreatureDefense.HurtByFrost << i)) == 0)
            {
                continue;
            }

            left &= ~(CreatureDefense.HurtByFrost << i);

            if (first)
            {
                page.Write(" It is susceptible to ");
                first = false;
            }
            else
            {
                page.Write((left & CreatureDefense.Weakness) != 0 ? ", " : " and ");
            }

            page.Write(GameTables.Weaknesses[i]);
        }

        if (!first)
        {
            page.Write(".");
        }

        if ((defense & CreatureDefense.Infravision) != 0)
        {
            page.Write(" It is warm blooded");
        }

        if ((defense & CreatureDefense.NeverSleeps) != 0)
        {
            page.Write((defense & CreatureDefense.Infravision) != 0 ? ", and" : " It");
            page.Write(" cannot be charmed or slept");
        }

        if ((defense & (CreatureDefense.NeverSleeps | CreatureDefense.Infravision)) != 0)
        {
            page.Write(".");
        }
    }

    /// <summary>
    /// How easily it notices the player. Known once it has been crept past
    /// enough times, or a wizard has looked, or it never sleeps and enough have
    /// been killed to have noticed that.
    /// </summary>
    private static void WriteAwareness(
        Page page, MonsterMemory memory, CreatureType creature)
    {
        if ((memory.Wake * memory.Wake) <= creature.Sleep
            && memory.Ignore != MaxByte
            && !(creature.Sleep == 0 && memory.Kills >= 10))
        {
            return;
        }

        page.Write(" It ");

        page.Write(creature.Sleep switch
        {
            > 200 => "prefers to ignore",
            > 95 => "pays very little attention to",
            > 75 => "pays little attention to",
            > 45 => "tends to overlook",
            > 25 => "takes quite a while to see",
            > 10 => "takes a while to see",
            > 5 => "is fairly observant of",
            > 3 => "is observant of",
            > 1 => "is very observant of",
            1 => "is vigilant for",
            _ => "is ever vigilant for",
        });

        page.Write(" intruders, which it may notice from "
            + Count(10 * creature.AreaOfEffect) + " feet.");
    }

    /// <summary>
    /// What it might be carrying, which is learned by killing it and looking.
    /// </summary>
    private static void WriteTreasure(Page page, CreatureType creature, uint move)
    {
        if ((move & (CreatureMove.CarriesObject | CreatureMove.CarriesGold)) == 0)
        {
            return;
        }

        page.Write(" It may");

        uint drops = (move & CreatureMove.Treasure) >> CreatureMove.TreasureShift;

        if (drops == 1)
        {
            page.Write((creature.MoveFlags & CreatureMove.Treasure)
                == CreatureMove.Drop60Percent
                ? " sometimes"
                : " often");
        }
        else if (drops == 2
            && (creature.MoveFlags & CreatureMove.Treasure)
                == (CreatureMove.Drop60Percent | CreatureMove.Drop90Percent))
        {
            page.Write(" often");
        }

        page.Write(" carry");

        string what = (move & CreatureMove.SmallObject) != 0
            ? " small objects"
            : " objects";

        if (drops == 1)
        {
            what = (move & CreatureMove.SmallObject) != 0
                ? " a small object"
                : " an object";
        }
        else if (drops == 2)
        {
            page.Write(" one or two");
        }
        else
        {
            page.Write(" up to " + drops.ToString(CultureInfo.InvariantCulture));
        }

        if ((move & CreatureMove.CarriesObject) != 0)
        {
            page.Write(what);

            if ((move & CreatureMove.CarriesGold) != 0)
            {
                page.Write(" or treasure");

                if (drops > 1)
                {
                    page.Write("s");
                }
            }

            page.Write(".");
            return;
        }

        page.Write(drops != 1 ? " treasures." : " treasure.");
    }

    /// <summary>
    /// What it does when it reaches the player, and how hard - the second only
    /// once the blow has landed often enough to have been measured.
    /// </summary>
    private static void WriteAttacks(
        Page page, MonsterMemory memory, CreatureType creature)
    {
        // How many are known at all, which decides where the "and" goes.
        int known = 0;

        foreach (byte landed in memory.Attacks)
        {
            if (landed != 0)
            {
                known++;
            }
        }

        int said = 0;
        ReadOnlySpan<byte> attacks = creature.Attacks;

        for (int i = 0; i < attacks.Length && i < MonsterMemory.MaxAttacks; i++)
        {
            if (attacks[i] == 0)
            {
                break;
            }

            // Nothing is said about an attack that has never landed.
            if (memory.Attacks[i] == 0)
            {
                continue;
            }

            MonsterAttackType attack = GameTables.MonsterAttacks[attacks[i]];

            int effect = attack.Type;
            int method = attack.Description;
            int dice = attack.Dice;
            int sides = attack.Sides;

            said++;

            page.Write(said == 1 ? " It can " : said == known ? ", and " : ", ");

            if (method > 19)
            {
                method = 0;
            }

            page.Write(GameTables.AttackMethods[method]);

            if (effect == 1 && (dice <= 0 || sides <= 0))
            {
                continue;
            }

            page.Write(" to ");

            if (effect > 24)
            {
                effect = 0;
            }

            page.Write(GameTables.AttackEffects[effect]);

            if (dice == 0 || sides == 0
                || !KnowsDamage(creature.Level, memory.Attacks[i], dice * sides))
            {
                continue;
            }

            // Losing experience is done "by" so much rather than "with damage".
            page.Write(effect == 19 ? " by" : " with damage");
            page.Write(" " + Count(dice) + "d" + Count(sides));
        }

        if (said != 0)
        {
            page.Write(".");
        }
        else if (known > 0 && memory.Attacks[0] >= 10)
        {
            page.Write(" It has no physical attacks.");
        }
        else
        {
            page.Write(" Nothing is known about its attack.");
        }
    }

    private static string Count(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Plural(int count, string one, string many) =>
        count == 1 ? one : many;

    /// <summary>"st", "nd", "rd" or "th", with the teens all taking "th".</summary>
    private static string Ordinal(int level)
    {
        if (level / 10 == 1)
        {
            return "th";
        }

        return (level % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
    }

    /// <summary>
    /// The "n" of "an", for the levels whose names begin with a vowel sound.
    /// The original lists them rather than working them out.
    /// </summary>
    private static string AnBefore(int level) =>
        level is 8 or 11 or 18 ? "n" : string.Empty;

    /// <summary>
    /// Fills lines as the text arrives, breaking at the last space that fits.
    /// Mirrors roff().
    /// </summary>
    private sealed class Page(Display display)
    {
        /// <summary>The width of Umoria's vtype, which is the line buffer.</summary>
        private const int Width = 80;

        private readonly StringBuilder _buffer = new();

        /// <summary>Which screen line the next full line goes on.</summary>
        public int Line { get; private set; }

        public void Write(string text)
        {
            foreach (char letter in text)
            {
                if (letter != '\n' && _buffer.Length < Width - 1)
                {
                    _buffer.Append(letter);
                    continue;
                }

                // The line is full, or a break was asked for. Everything up to
                // the last space goes out; what follows it starts the next line.
                _buffer.Append(letter);

                int at = _buffer.Length - 1;

                if (letter != '\n')
                {
                    while (at > 0 && _buffer[at] != ' ')
                    {
                        at--;
                    }
                }

                display.Print(_buffer.ToString(0, at), Line, 0);
                Line++;

                string carried = _buffer.ToString(at + 1, _buffer.Length - at - 1);
                _buffer.Clear();
                _buffer.Append(carried);
            }
        }
    }
}
