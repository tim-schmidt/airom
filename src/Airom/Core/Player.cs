// The player's own state, and the parts of character creation that roll.
//
// Ported from the misc and stats blocks of player_type in Umoria 5.6
// source/types.h, the generation half of source/create.c, and the stat
// adjustment helpers in source/misc3.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The player character. Mirrors the parts of Umoria's player_type that
/// character creation fills in.
/// </summary>
public sealed class Player
{
    /// <summary>Umoria's MAX_PLAYER_LEVEL.</summary>
    public const int MaxLevel = 40;

    public string Name { get; set; } = string.Empty;

    public bool Male { get; set; } = true;

    /// <summary>Index into <see cref="GameTables.Races"/>.</summary>
    public int Race { get; set; }

    /// <summary>Index into <see cref="GameTables.Classes"/>.</summary>
    public int Class { get; set; }

    /// <summary>
    /// The unmodified stats, which is what a drained stat is restored to.
    /// Values run 3 to 118, where anything above 18 is Umoria's way of writing
    /// the percentile range 18/01 to 18/100.
    /// </summary>
    public int[] MaxStat { get; } = new int[Stat.Count];

    /// <summary>The current values, which drain and restore.</summary>
    public int[] CurrentStat { get; } = new int[Stat.Count];

    /// <summary>Adjustments from equipment and effects.</summary>
    public int[] ModStat { get; } = new int[Stat.Count];

    /// <summary>What the game actually uses: current, plus modifiers.</summary>
    public int[] UseStat { get; } = new int[Stat.Count];

    public int Level { get; set; } = 1;

    public int Gold { get; set; }

    public int Age { get; set; }

    public int Height { get; set; }

    public int Weight { get; set; }

    /// <summary>Social class, 1 to 100, which decides starting money.</summary>
    public int SocialClass { get; set; }

    /// <summary>Four lines of rolled background text.</summary>
    public string[] History { get; } = ["", "", "", ""];

    public int Search { get; set; }

    public int SearchFrequency { get; set; }

    public int BaseToHit { get; set; }

    public int BaseToHitBows { get; set; }

    public int Disarm { get; set; }

    public int Stealth { get; set; }

    public int Save { get; set; }

    public int HitDie { get; set; }

    public int ExperienceFactor { get; set; }

    public int Infravision { get; set; }

    public int MaxHitPoints { get; set; }

    public int CurrentHitPoints { get; set; }

    public int PlusToHit { get; set; }

    public int PlusToDamage { get; set; }

    public int PlusToArmourClass { get; set; }

    public int ArmourClass { get; set; }

    /// <summary>
    /// Total hit points at each level, rolled once at creation. Umoria fixes the
    /// whole curve up front so that levelling cannot be re-rolled by reloading.
    /// </summary>
    public int[] HitPointsByLevel { get; } = new int[MaxLevel];

    /// <summary>Experience earned. Umoria's exp.</summary>
    public int Experience { get; set; }

    /// <summary>Spell points available now.</summary>
    public int CurrentMana { get; set; }

    /// <summary>Armour class as shown, which includes the magical bonus.</summary>
    public int DisplayedArmourClass { get; set; }

    /// <summary>Conditions the player is under; see <see cref="PlayerStatus"/>.</summary>
    public uint Status { get; set; }

    /// <summary>Speed adjustment. Positive is slower, which reads backwards but is Umoria's.</summary>
    public int Speed { get; set; }

    /// <summary>Turns left paralysed.</summary>
    public int Paralysis { get; set; }

    /// <summary>Turns left resting, or negative to rest until something happens.</summary>
    public int Rest { get; set; }

    /// <summary>Spells waiting to be learned.</summary>
    public int NewSpells { get; set; }
}

/// <summary>
/// The rolling half of character creation. Mirrors create.c, less the prompting.
///
/// Umoria's create_character() interleaves generation with screen prompts for
/// race, sex and class. Those choices are inputs here rather than questions, so
/// the generation can be driven and compared without a terminal - the prompting
/// belongs with the rest of the interface.
/// </summary>
public sealed class CharacterCreation(GameState game)
{
    private readonly GameState _game = game;

    private Rng Rng => _game.Rng;

    /// <summary>
    /// Rolls the six stats. Mirrors get_stats().
    ///
    /// Eighteen dice of three, four and five sides, three to a stat, re-rolled
    /// entirely unless the total lands strictly between 42 and 54. That narrow
    /// band is what stops a hopeless character ever being offered.
    /// </summary>
    public void RollStats(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var dice = new int[18];
        int total;

        do
        {
            total = 0;
            for (int i = 0; i < dice.Length; i++)
            {
                // Sides cycle 3, 4, 5 so each stat gets one of each.
                dice[i] = Rng.RandInt(3 + (i % 3));
                total += dice[i];
            }
        }
        while (total <= 42 || total >= 54);

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 5 + dice[3 * i] + dice[(3 * i) + 1] + dice[(3 * i) + 2];
        }
    }

    /// <summary>
    /// Shifts one stat by a race or class adjustment. Mirrors change_stat().
    ///
    /// The scale is not linear. Below 18 a point is a point; above it the values
    /// stand for Umoria's percentile range and move in jumps, so the same
    /// adjustment is worth far more to a weak character than a strong one. That
    /// is also why this consumes randomness rather than simply adding.
    /// </summary>
    public void ChangeStat(Player player, int stat, int amount)
    {
        ArgumentNullException.ThrowIfNull(player);

        int value = player.MaxStat[stat];

        if (amount < 0)
        {
            for (int i = 0; i > amount; i--)
            {
                if (value > 108)
                {
                    value--;
                }
                else if (value > 88)
                {
                    value += -Rng.RandInt(6) - 2;
                }
                else if (value > 18)
                {
                    value += -Rng.RandInt(15) - 5;
                    if (value < 18)
                    {
                        value = 18;
                    }
                }
                else if (value > 3)
                {
                    value--;
                }
            }
        }
        else
        {
            for (int i = 0; i < amount; i++)
            {
                if (value < 18)
                {
                    value++;
                }
                else if (value < 88)
                {
                    value += Rng.RandInt(15) + 5;
                }
                else if (value < 108)
                {
                    value += Rng.RandInt(6) + 2;
                }
                else if (value < 118)
                {
                    value++;
                }
            }
        }

        player.MaxStat[stat] = value;
    }

    /// <summary>
    /// Applies equipment modifiers to a stat. Mirrors modify_stat().
    /// At creation nothing is worn, so this simply copies the current value.
    /// </summary>
    private static int ModifyStat(Player player, int stat, int amount)
    {
        int value = player.CurrentStat[stat];
        int steps = Math.Abs(amount);

        for (int i = 0; i < steps; i++)
        {
            if (amount > 0)
            {
                if (value < 18)
                {
                    value++;
                }
                else if (value < 108)
                {
                    value += 10;
                }
                else
                {
                    value = 118;
                }
            }
            else if (value > 27)
            {
                value -= 10;
            }
            else if (value > 18)
            {
                value = 18;
            }
            else if (value > 3)
            {
                value--;
            }
        }

        return value;
    }

    /// <summary>Recomputes the effective value of every stat. Mirrors set_use_stat().</summary>
    private static void SetUseStats(Player player)
    {
        for (int i = 0; i < Stat.Count; i++)
        {
            player.CurrentStat[i] = player.MaxStat[i];
            player.UseStat[i] = ModifyStat(player, i, player.ModStat[i]);
        }
    }

    /// <summary>
    /// Rolls the stats and applies the race. Mirrors get_all_stats().
    /// </summary>
    public void RollForRace(Player player, int race)
    {
        ArgumentNullException.ThrowIfNull(player);

        player.Race = race;
        RaceType kind = GameTables.Races[race];

        RollStats(player);
        for (int i = 0; i < Stat.Count; i++)
        {
            ChangeStat(player, i, kind.StatAdjust[i]);
        }

        player.Level = 1;
        SetUseStats(player);

        player.Search = kind.Search;
        player.BaseToHit = kind.BaseToHit;
        player.BaseToHitBows = kind.BaseToHitBows;
        player.SearchFrequency = kind.SearchFrequency;
        player.Stealth = kind.Stealth;
        player.Save = kind.BaseSave;
        player.HitDie = kind.BaseHitDie;
        player.PlusToDamage = DamageBonus(player);
        player.PlusToHit = HitBonus(player);
        player.PlusToArmourClass = 0;
        player.ArmourClass = ArmourBonus(player);
        player.ExperienceFactor = kind.ExperienceFactor;
        player.Infravision = kind.Infravision;
    }

    /// <summary>
    /// Rolls a background and the social class that comes with it. Mirrors
    /// get_history().
    ///
    /// The table is a set of linked charts. Each roll picks an entry from the
    /// current chart, appends its text and its social standing, then moves to
    /// whichever chart that entry points at - so a history is assembled a clause
    /// at a time and its length varies with the path taken.
    /// </summary>
    public void RollHistory(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        // Each race starts on its own chart, three apart.
        int chart = (player.Race * 3) + 1;
        int socialClass = Rng.RandInt(4);
        var text = new System.Text.StringBuilder();
        int cursor = 0;

        do
        {
            bool matched = false;
            do
            {
                if (GameTables.Backgrounds[cursor].Chart == chart)
                {
                    int roll = Rng.RandInt(100);
                    while (roll > GameTables.Backgrounds[cursor].Roll)
                    {
                        cursor++;
                    }

                    BackgroundType entry = GameTables.Backgrounds[cursor];
                    text.Append(entry.Text);
                    socialClass += entry.Bonus - 50;

                    // Charts can point backwards, in which case the search
                    // restarts from the top of the table.
                    if (chart > entry.Next)
                    {
                        cursor = 0;
                    }

                    chart = entry.Next;
                    matched = true;
                }
                else
                {
                    cursor++;
                }
            }
            while (!matched);
        }
        while (chart >= 1);

        WrapHistory(player, text.ToString());

        player.SocialClass = Math.Clamp(socialClass, 1, 100);
    }

    /// <summary>
    /// Breaks the assembled history into lines of at most sixty characters,
    /// splitting on spaces. Mirrors the formatting half of get_history().
    /// </summary>
    private static void WrapHistory(Player player, string block)
    {
        for (int i = 0; i < player.History.Length; i++)
        {
            player.History[i] = string.Empty;
        }

        int end = block.Length - 1;
        while (end >= 0 && block[end] == ' ')
        {
            end--;
        }

        int start = 0;
        int line = 0;
        bool last = false;

        while (!last && line < player.History.Length)
        {
            while (start <= end && block[start] == ' ')
            {
                start++;
            }

            int length = end - start + 1;
            int nextStart = start;

            if (length > 60)
            {
                length = 60;
                while (block[start + length - 1] != ' ')
                {
                    length--;
                }

                nextStart = start + length;
                while (block[start + length - 1] == ' ')
                {
                    length--;
                }
            }
            else
            {
                last = true;
            }

            player.History[line] = block.Substring(start, Math.Max(length, 0));
            line++;
            start = nextStart;
        }
    }

    /// <summary>
    /// Rolls age, height and weight. Mirrors get_ahw().
    ///
    /// Height and weight come from a normal distribution around the race's
    /// build, which is why a Half-Troll is reliably enormous rather than
    /// uniformly random.
    /// </summary>
    public void RollAgeHeightWeight(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        RaceType kind = GameTables.Races[player.Race];

        player.Age = kind.BaseAge + Rng.RandInt(kind.AgeRange);

        if (player.Male)
        {
            player.Height = Rng.RandNor(kind.MaleBaseHeight, kind.MaleHeightRange);
            player.Weight = Rng.RandNor(kind.MaleBaseWeight, kind.MaleWeightRange);
        }
        else
        {
            player.Height = Rng.RandNor(kind.FemaleBaseHeight, kind.FemaleHeightRange);
            player.Weight = Rng.RandNor(kind.FemaleBaseWeight, kind.FemaleWeightRange);
        }

        player.Disarm = kind.BaseDisarm + DisarmBonus(player);
    }

    /// <summary>
    /// Applies a class and rolls the hit point curve. Mirrors the second half of
    /// get_class(), after the prompt.
    /// </summary>
    public void ApplyClass(Player player, int characterClass)
    {
        ArgumentNullException.ThrowIfNull(player);

        player.Class = characterClass;
        ClassType kind = GameTables.Classes[characterClass];

        for (int i = 0; i < Stat.Count; i++)
        {
            ChangeStat(player, i, kind.StatAdjust[i]);
        }

        SetUseStats(player);

        player.PlusToDamage = DamageBonus(player);
        player.PlusToHit = HitBonus(player);
        player.PlusToArmourClass = ArmourBonus(player);
        player.ArmourClass = 0;

        // Constitution must be settled before hit points are worked out.
        player.HitDie += kind.HitDieAdjust;
        player.MaxHitPoints = ConstitutionBonus(player) + player.HitDie;
        player.CurrentHitPoints = player.MaxHitPoints;

        RollHitPointCurve(player);

        player.BaseToHit += kind.BaseToHit;
        player.BaseToHitBows += kind.BaseToHitBows;
        player.Search += kind.Search;
        player.Disarm += kind.Disarm;
        player.SearchFrequency += kind.SearchFrequency;
        player.Stealth += kind.Stealth;
        player.Save += kind.Save;
        player.ExperienceFactor += kind.ExperienceFactor;
    }

    /// <summary>
    /// Rolls total hit points for every level at once. Mirrors the loop in
    /// get_class().
    ///
    /// The whole curve is fixed at creation rather than rolled on levelling, and
    /// re-rolled entirely unless the total lands within an eighth of average -
    /// so no character is stuck with a run of bad rolls, and none gets a
    /// remarkable one.
    /// </summary>
    private void RollHitPointCurve(Player player)
    {
        int minimum = (Player.MaxLevel * 3 / 8 * (player.HitDie - 1)) + Player.MaxLevel;
        int maximum = (Player.MaxLevel * 5 / 8 * (player.HitDie - 1)) + Player.MaxLevel;

        player.HitPointsByLevel[0] = player.HitDie;

        do
        {
            for (int i = 1; i < Player.MaxLevel; i++)
            {
                player.HitPointsByLevel[i] =
                    Rng.RandInt(player.HitDie) + player.HitPointsByLevel[i - 1];
            }
        }
        while (player.HitPointsByLevel[Player.MaxLevel - 1] < minimum
            || player.HitPointsByLevel[Player.MaxLevel - 1] > maximum);
    }

    /// <summary>
    /// Works out starting money. Mirrors get_money().
    ///
    /// Higher stats mean less gold, which is the original's way of balancing a
    /// strong roll - except charisma, which adds. Women start with fifty more,
    /// the source noting she "charmed the banker into it".
    /// </summary>
    public void RollMoney(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        int statValue = MoneyValue(player.MaxStat[Stat.Strength])
            + MoneyValue(player.MaxStat[Stat.Intelligence])
            + MoneyValue(player.MaxStat[Stat.Wisdom])
            + MoneyValue(player.MaxStat[Stat.Constitution])
            + MoneyValue(player.MaxStat[Stat.Dexterity]);

        int gold = (player.SocialClass * 6) + Rng.RandInt(25) + 325;
        gold -= statValue;
        gold += MoneyValue(player.MaxStat[Stat.Charisma]);

        if (!player.Male)
        {
            gold += 50;
        }

        player.Gold = Math.Max(gold, 80);
    }

    private static int MoneyValue(int stat) => 5 * (stat - 10);

    // -------------------------------------------------------- stat bonuses

    /// <summary>Damage bonus from strength. Mirrors todam_adj().</summary>
    private static int DamageBonus(Player player) => player.UseStat[Stat.Strength] switch
    {
        < 4 => -2,
        < 5 => -1,
        < 16 => 0,
        < 17 => 1,
        < 18 => 2,
        < 94 => 3,
        < 109 => 4,
        < 117 => 5,
        _ => 6,
    };

    /// <summary>To-hit bonus from dexterity and strength together. Mirrors tohit_adj().</summary>
    private static int HitBonus(Player player)
    {
        int total = player.UseStat[Stat.Dexterity] switch
        {
            < 4 => -3,
            < 6 => -2,
            < 8 => -1,
            < 16 => 0,
            < 17 => 1,
            < 18 => 2,
            < 69 => 3,
            < 118 => 4,
            _ => 5,
        };

        return total + player.UseStat[Stat.Strength] switch
        {
            < 4 => -3,
            < 5 => -2,
            < 7 => -1,
            < 18 => 0,
            < 94 => 1,
            < 109 => 2,
            < 117 => 3,
            _ => 4,
        };
    }

    /// <summary>Armour class bonus from dexterity. Mirrors toac_adj().</summary>
    private static int ArmourBonus(Player player) => player.UseStat[Stat.Dexterity] switch
    {
        < 4 => -4,
        4 => -3,
        5 => -2,
        6 => -1,
        < 15 => 0,
        < 18 => 1,
        < 59 => 2,
        < 94 => 3,
        < 117 => 4,
        _ => 5,
    };

    /// <summary>Disarming bonus from dexterity. Mirrors todis_adj().</summary>
    private static int DisarmBonus(Player player) => player.UseStat[Stat.Dexterity] switch
    {
        < 4 => -8,
        4 => -6,
        5 => -4,
        6 => -2,
        7 => -1,
        < 13 => 0,
        < 16 => 1,
        < 18 => 2,
        < 59 => 4,
        < 94 => 5,
        < 117 => 6,
        _ => 8,
    };

    /// <summary>Hit point bonus from constitution. Mirrors con_adj().</summary>
    private static int ConstitutionBonus(Player player)
    {
        int con = player.UseStat[Stat.Constitution];
        return con switch
        {
            < 7 => con - 7,
            < 17 => 0,
            17 => 1,
            < 94 => 2,
            < 117 => 3,
            _ => 4,
        };
    }

    /// <summary>
    /// Which classes a race may take, as indices into
    /// <see cref="GameTables.Classes"/>. Mirrors the filter get_class() applies
    /// when it builds its menu.
    ///
    /// Umoria stores this as a bit field per race, and the prompt letter indexes
    /// the filtered list rather than the class table - so "c" means a different
    /// class depending on who is asking.
    /// </summary>
    public static IReadOnlyList<int> AllowedClasses(int race)
    {
        byte mask = GameTables.Races[race].AllowedClasses;
        List<int> allowed = [];

        for (int i = 0; i < GameTables.Classes.Length; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                allowed.Add(i);
            }
        }

        return allowed;
    }

    /// <summary>
    /// Creates a character. Mirrors create_character(), less the prompting.
    ///
    /// The order matters: the race is applied to the rolled stats, the history
    /// and build follow, then the class adjusts the stats again before hit
    /// points are worked out, and money is settled last from the final stats.
    /// </summary>
    public Player Create(int race, int characterClass, bool male, string name)
    {
        if (!AllowedClasses(race).Contains(characterClass))
        {
            throw new ArgumentException(
                $"{GameTables.Races[race].Name} cannot be a "
                + $"{GameTables.Classes[characterClass].Title}.",
                nameof(characterClass));
        }

        var player = new Player { Male = male, Name = name };

        RollForRace(player, race);
        RollHistory(player);
        RollAgeHeightWeight(player);
        ApplyClass(player, characterClass);
        RollMoney(player);

        return player;
    }
}
