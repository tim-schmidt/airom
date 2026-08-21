// Ported from Umoria 5.6 source/wizard.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The debugging commands, which only wizard mode reaches.
///
/// They are part of the game rather than a tool bolted on: entering wizard mode
/// forfeits the score, and these are what it is forfeited for. Nothing here is
/// careful - the item builder will happily make something the rest of the game
/// cannot describe - and the original says as much before it starts.
/// </summary>
public class WizardCommands
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public WizardCommands(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    private Player Player => _game.Player;

    /// <summary>The blank object a hand-built item starts from. Umoria's OBJ_WIZARD.</summary>
    private const int WizardObject = 419;

    /// <summary>
    /// Lights the whole level, or puts it back into the dark. Mirrors
    /// wizard_light().
    ///
    /// Which of the two it does depends on the square the player is standing
    /// on, so the same command toggles.
    /// </summary>
    public void LightLevel()
    {
        bool lighting = !_game.Cave[_game.CharacterRow, _game.CharacterColumn]
            .PermanentLight;

        for (int row = 0; row < _game.Cave.Height; row++)
        {
            for (int column = 0; column < _game.Cave.Width; column++)
            {
                if (_game.Cave[row, column].Feature > CaveFeature.MaxCaveFloor)
                {
                    continue;
                }

                // The floor square and everything touching it, so the walls
                // around a room are lit along with it.
                for (int i = row - 1; i <= row + 1; i++)
                {
                    for (int j = column - 1; j <= column + 1; j++)
                    {
                        CaveSquare square = _game.Cave[i, j];
                        square.PermanentLight = lighting;

                        if (!lighting)
                        {
                            square.FieldMark = false;
                        }
                    }
                }
            }
        }

        _display.PrintMap();
    }

    /// <summary>
    /// Edits the character. Mirrors change_character().
    ///
    /// Every question is asked in turn and backing out of any of them abandons
    /// the rest, so a wizard who only wants to change the strength presses
    /// escape at the second question.
    /// </summary>
    public void ChangeCharacter()
    {
        for (int stat = 0; stat < Stat.Count; stat++)
        {
            if (!Ask("(3 - 118) " + StatPrompts[stat] + " = ", 25, 3, out string typed))
            {
                return;
            }

            int value = ParseNumber(typed);

            if (value > 2 && value < 119)
            {
                Player.MaxStat[stat] = value;
                _loop.Stats.Restore(stat);
            }
        }

        if (!Ask("(1 - 32767) Hit points = ", 25, 5, out string hitPoints))
        {
            return;
        }

        int points = ParseNumber(hitPoints);

        if (points > 0 && points <= GameLoop.MaxShort)
        {
            Player.MaxHitPoints = points;
            Player.CurrentHitPoints = points;
            Player.HitPointFraction = 0;
            _display.PrintMaxHitPoints(Player);
            _display.PrintCurrentHitPoints(Player);
        }

        if (!Ask("(0 - 32767) Mana       = ", 25, 5, out string mana))
        {
            return;
        }

        int points2 = ParseNumber(mana);

        if (points2 > -1 && points2 <= GameLoop.MaxShort && mana.Length > 0)
        {
            Player.MaxMana = points2;
            Player.CurrentMana = points2;
            Player.ManaFraction = 0;
            _display.PrintCurrentMana(Player);
        }

        if (!AskWithCurrent("Current=" + N(Player.Gold) + "  Gold = ", 7, out string gold))
        {
            return;
        }

        int purse = ParseNumber(gold);

        if (purse > -1 && gold.Length > 0)
        {
            Player.Gold = purse;
            _display.PrintGold(Player);
        }

        if (!EditSkill("Current=" + N(Player.Search) + "  (0-200) Searching = ",
                       -1, 201, out int search))
        {
            return;
        }

        Keep(ref search, Player.Search);
        Player.Search = search;

        if (!EditSkill("Current=" + N(Player.Stealth) + "  (-1-18) Stealth = ",
                       -2, 19, out int stealth))
        {
            return;
        }

        Keep(ref stealth, Player.Stealth);
        Player.Stealth = stealth;

        if (!EditSkill("Current=" + N(Player.Disarm) + "  (0-200) Disarming = ",
                       -1, 201, out int disarm))
        {
            return;
        }

        Keep(ref disarm, Player.Disarm);
        Player.Disarm = disarm;

        // FAITHFUL QUIRK: the saving throw is checked against the same bounds
        // as the skills above rather than the nought-to-a-hundred its prompt
        // promises, so a wizard may set it to two hundred.
        if (!EditSkill("Current=" + N(Player.Save) + "  (0-100) Save = ",
                       -1, 201, out int save))
        {
            return;
        }

        Keep(ref save, Player.Save);
        Player.Save = save;

        if (!EditSkill("Current=" + N(Player.BaseToHit) + "  (0-200) Base to hit = ",
                       -1, 201, out int toHit))
        {
            return;
        }

        Keep(ref toHit, Player.BaseToHit);
        Player.BaseToHit = toHit;

        if (!EditSkill(
                "Current=" + N(Player.BaseToHitBows) + "  (0-200) Bows/Throwing = ",
                -1, 201, out int bows))
        {
            return;
        }

        Keep(ref bows, Player.BaseToHitBows);
        Player.BaseToHitBows = bows;

        if (!AskWithCurrent("Current=" + N(Player.Weight) + "  Weight = ", 3,
                            out string weight))
        {
            return;
        }

        int carried = ParseNumber(weight);

        if (carried > -1 && weight.Length > 0)
        {
            Player.Weight = carried;
        }

        // And last, the speed, which is asked over and over until something
        // that is not a sign is typed.
        while (_display.GetCommand("Alter speed? (+/-)", out char key))
        {
            if (key == '+')
            {
                _loop.ChangeSpeed(-1);
            }
            else if (key == '-')
            {
                _loop.ChangeSpeed(1);
            }
            else
            {
                break;
            }

            _display.PrintSpeed(Player);
        }
    }

    /// <summary>
    /// Puts back what was there when nothing usable was typed, which is how
    /// each of these questions may be passed over without changing anything.
    /// </summary>
    private static void Keep(ref int value, int current)
    {
        if (value == Unchanged)
        {
            value = current;
        }
    }

    /// <summary>What an answer that changes nothing comes back as.</summary>
    private const int Unchanged = int.MinValue;

    /// <summary>The six stats, in the order the questions are asked.</summary>
    private static readonly string[] StatPrompts =
    [
        "Strength    ", "Intelligence", "Wisdom      ",
        "Dexterity   ", "Constitution", "Charisma    ",
    ];

    /// <summary>
    /// One of the skill questions, which all share a shape: a bound either
    /// side, and nothing typed at all leaves the value alone.
    /// </summary>
    /// <returns>False when the player backed out and the rest is abandoned.</returns>
    private bool EditSkill(string prompt, int above, int below, out int value)
    {
        value = Unchanged;

        if (!AskWithCurrent(prompt, 3, out string typed))
        {
            return false;
        }

        int typedValue = ParseNumber(typed);

        if (typedValue > above && typedValue < below && typed.Length > 0)
        {
            value = typedValue;
        }

        return true;
    }

    /// <summary>
    /// Builds an item by hand and puts it underfoot. Mirrors wizard_create().
    ///
    /// Nothing is checked: the numbers go straight into the item, which is what
    /// the warning is for.
    /// </summary>
    public void CreateObject()
    {
        _display.MessagePrint("Warning: This routine can cause a fatal error.");

        var forged = new InvenType();
        forged.CopyFrom(WizardObject);
        forged.SpecialName = 0;
        forged.Inscription = "wizard item";
        forged.Identification = Identification.Known | Identification.StoreBought;

        if (!Ask("Tval   : ", 9, 3, out string category))
        {
            return;
        }

        forged.TVal = (byte)ParseNumber(category);

        if (!Ask("Tchar  : ", 9, 1, out string symbol))
        {
            return;
        }

        forged.DisplayChar = symbol.Length > 0 ? symbol[0] : '\0';

        if (!Ask("Subval : ", 9, 5, out string kind))
        {
            return;
        }

        forged.SubVal = (byte)ParseNumber(kind);

        if (!Ask("Weight : ", 9, 5, out string weight))
        {
            return;
        }

        forged.Weight = (ushort)ParseNumber(weight);

        if (!Ask("Number : ", 9, 5, out string number))
        {
            return;
        }

        forged.Number = (byte)ParseNumber(number);

        if (!Ask("Damage (dice): ", 15, 3, out string dice))
        {
            return;
        }

        forged.DamageDice = (byte)ParseNumber(dice);

        if (!Ask("Damage (sides): ", 16, 3, out string sides))
        {
            return;
        }

        forged.DamageSides = (byte)ParseNumber(sides);

        if (!Ask("+To hit: ", 9, 3, out string toHit))
        {
            return;
        }

        forged.ToHit = (short)ParseNumber(toHit);

        if (!Ask("+To dam: ", 9, 3, out string toDamage))
        {
            return;
        }

        forged.ToDam = (short)ParseNumber(toDamage);

        if (!Ask("AC     : ", 9, 3, out string armour))
        {
            return;
        }

        forged.Ac = (short)ParseNumber(armour);

        if (!Ask("+To AC : ", 9, 3, out string toArmour))
        {
            return;
        }

        forged.ToAc = (short)ParseNumber(toArmour);

        if (!Ask("P1     : ", 9, 5, out string p1))
        {
            return;
        }

        forged.P1 = (short)ParseNumber(p1);

        if (!Ask("Flags (In HEX): ", 16, 8, out string flags))
        {
            return;
        }

        forged.Flags = ParseHex(flags);

        if (!Ask("Cost : ", 9, 8, out string cost))
        {
            return;
        }

        forged.Cost = ParseNumber(cost);

        if (!Ask("Level : ", 10, 3, out string level))
        {
            return;
        }

        forged.Level = (byte)ParseNumber(level);

        if (!_display.GetCheck("Allocate?"))
        {
            _display.MessagePrint("Aborted.");
            return;
        }

        // Whatever was underfoot goes first, before a slot is taken for this.
        CaveSquare square = _game.Cave[_game.CharacterRow, _game.CharacterColumn];

        if (square.ObjectIndex != 0)
        {
            _loop.Movement.DeleteObject(_game.CharacterRow, _game.CharacterColumn);
        }

        int index = _game.Objects.Allocate();
        _game.Objects[index].CopyStateFrom(forged);
        square.ObjectIndex = index;

        _display.MessagePrint("Allocated.");
    }

    /// <summary>Puts a question on the message line and reads the answer.</summary>
    private bool Ask(string prompt, int column, int length, out string typed)
    {
        _display.Print(prompt, 0, 0);
        return _display.GetString(0, column, length, out typed);
    }

    /// <summary>
    /// The same, for the questions that show the value they are replacing: the
    /// answer is typed at the end of the prompt rather than at a fixed column.
    /// </summary>
    private bool AskWithCurrent(string prompt, int length, out string typed)
    {
        _display.Print(prompt, 0, 0);
        return _display.GetString(0, prompt.Length, length, out typed);
    }

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// What atoi() makes of what was typed: the leading number, or nothing when
    /// it does not start with one.
    /// </summary>
    private static int ParseNumber(string text)
    {
        int at = 0;

        while (at < text.Length && char.IsWhiteSpace(text[at]))
        {
            at++;
        }

        int start = at;

        if (at < text.Length && (text[at] == '+' || text[at] == '-'))
        {
            at++;
        }

        int digits = at;

        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            at++;
        }

        if (at == digits)
        {
            return 0;
        }

        return long.TryParse(
            text.AsSpan(start, at - start), CultureInfo.InvariantCulture, out long value)
            ? (int)value
            : 0;
    }

    /// <summary>What sscanf's "%lx" makes of what was typed.</summary>
    private static uint ParseHex(string text)
    {
        int at = 0;

        while (at < text.Length && char.IsWhiteSpace(text[at]))
        {
            at++;
        }

        int start = at;

        while (at < text.Length && char.IsAsciiHexDigit(text[at]))
        {
            at++;
        }

        return at == start
            ? 0
            : uint.TryParse(
                text.AsSpan(start, at - start), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out uint value)
                ? value
                : 0;
    }
}
