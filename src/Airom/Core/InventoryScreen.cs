// Ported from the inventory half of Umoria 5.6 source/moria1.c - show_inven,
// show_equip, verify, inven_screen, inven_command and get_item.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The inventory screens, and the prompt that asks which item.
///
/// Both lists are drawn as far right as they will fit, so that as little of the
/// map as possible is covered and putting it back afterwards is cheap. The
/// column is worked out from the longest line and then used for all of them.
///
/// The inventory command is a small mode of its own: it stays up across several
/// keys, and some of those keys take a turn. Rather than run the rest of the
/// game from inside it, the original returns after a key that costs a turn and
/// is called again next turn, remembering where it was in
/// <see cref="ContinuingCommand"/>. That is reproduced here, quirks and all.
/// </summary>
public partial class InventoryScreen
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public InventoryScreen(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    private Player Player => _game.Player;

    private Inventory Pack => _game.Inventory;

    /// <summary>Which list is on the screen. Umoria's scr_state.</summary>
    private enum Screen
    {
        /// <summary>Nothing drawn: the command was answered from the message line.</summary>
        Blank = 0,
        Equipment = 1,
        Inventory = 2,

        /// <summary>The part of the pack that could be worn or wielded.</summary>
        Wearable = 3,
        Help = 4,

        /// <summary>Not a screen: forces the next draw, whatever was up before.</summary>
        Wrong = 5,
    }

    private Screen _screen;
    private int _left;
    private int _base;
    private int _wearLow;
    private int _wearHigh;

    /// <summary>
    /// The command to resume with next turn, or nothing when the mode is not
    /// half-finished. Mirrors Umoria's doing_inven.
    /// </summary>
    public char? ContinuingCommand { get; set; }

    // ----------------------------------------------------------- the lists

    /// <summary>
    /// Lists pack slots. Mirrors show_inven().
    ///
    /// <paramref name="column"/> is where the caller would like the list to
    /// start; it moves left if the longest line will not fit. Descriptions are
    /// cut rather than wrapped, since a list that wrapped would cover the map
    /// twice over.
    /// </summary>
    /// <param name="mask">
    /// When given, only the slots it marks are listed - which is how a prompt
    /// limited to one kind of thing lists only that kind.
    /// </param>
    /// <returns>The column actually used.</returns>
    public int ShowInventory(int first, int last, bool weight, int column, bool[]? mask)
    {
        int width = 79 - column;
        int limit = weight ? 68 : 76;
        var lines = new string[Inventory.Size];

        for (int i = first; i <= last; i++)
        {
            if (mask is not null && !mask[i])
            {
                continue;
            }

            string description = Truncate(
                _game.Names.Describe(Pack[i], withArticle: true), limit);

            lines[i] = (char)('a' + i) + ") " + description;

            int length = lines[i].Length + 2 + (weight ? 9 : 0);

            if (length > width)
            {
                width = length;
            }
        }

        column = Math.Max(0, 79 - width);

        int line = 1;

        for (int i = first; i <= last; i++)
        {
            if (mask is not null && !mask[i])
            {
                continue;
            }

            PrintEntry(lines[i], line, column);

            if (weight)
            {
                PrintWeight(Pack[i], line);
            }

            line++;
        }

        return column;
    }

    /// <summary>
    /// Lists what is worn and wielded. Mirrors show_equip().
    ///
    /// The letters run over the slots in use rather than over the slots there
    /// are, so an empty neck does not push a ring down the alphabet.
    /// </summary>
    /// <returns>The column actually used.</returns>
    public int ShowEquipment(bool weight, int column)
    {
        int width = 79 - column;
        int limit = weight ? 52 : 60;
        var lines = new string[Inventory.Size - Inventory.WieldSlot];
        int line = 0;

        for (int slot = Inventory.WieldSlot; slot < Inventory.Size; slot++)
        {
            InvenType worn = Pack[slot];

            if (worn.TVal == ItemCategory.Nothing)
            {
                continue;
            }

            string where = SlotHeading(slot, worn);
            string description = Truncate(
                _game.Names.Describe(worn, withArticle: true), limit);

            lines[line] = (char)('a' + line) + ") " + where.PadRight(14) + ": "
                + description;

            int length = lines[line].Length + 2 + (weight ? 9 : 0);

            if (length > width)
            {
                width = length;
            }

            line++;
        }

        column = Math.Max(0, 79 - width);

        line = 0;

        for (int slot = Inventory.WieldSlot; slot < Inventory.Size; slot++)
        {
            InvenType worn = Pack[slot];

            if (worn.TVal == ItemCategory.Nothing)
            {
                continue;
            }

            PrintEntry(lines[line], line + 1, column);

            if (weight)
            {
                PrintWeight(worn, line + 1);
            }

            line++;
        }

        _display.EraseLine(line + 1, column);
        return column;
    }

    /// <summary>
    /// How the equipment list labels a slot. A weapon too heavy to swing is
    /// only being lifted, which is the one label that depends on the player.
    /// </summary>
    private string SlotHeading(int slot, InvenType worn) => slot switch
    {
        Inventory.WieldSlot => Player.UseStat[Stat.Strength] * 15 < worn.Weight
            ? "Just lifting"
            : "Wielding",
        Inventory.HeadSlot => "On head",
        Inventory.NeckSlot => "Around neck",
        Inventory.BodySlot => "On body",
        Inventory.ArmSlot => "On arm",
        Inventory.HandsSlot => "On hands",
        Inventory.RightRingSlot => "On right hand",
        Inventory.LeftRingSlot => "On left hand",
        Inventory.FeetSlot => "On feet",
        Inventory.OuterSlot => "About body",
        Inventory.LightSlot => "Light source",
        Inventory.AuxiliarySlot => "Spare weapon",
        _ => "Unknown value",
    };

    /// <summary>
    /// One line of either list. The two leading spaces are dropped when the
    /// list is against the left edge, where there is nothing to separate it
    /// from.
    /// </summary>
    private void PrintEntry(string text, int line, int column)
    {
        if (column == 0)
        {
            _display.Print(text, line, column);
        }
        else
        {
            _display.PutBuffer("  ", line, column);
            _display.Print(text, line, column + 2);
        }
    }

    /// <summary>What a pile weighs, in pounds and tenths, in its own column.</summary>
    private void PrintWeight(InvenType item, int line)
    {
        int total = item.Weight * item.Number;

        _display.Print(
            (total / 10).ToString(CultureInfo.InvariantCulture).PadLeft(3)
            + "." + (total % 10).ToString(CultureInfo.InvariantCulture) + " lb",
            line, 71);
    }

    private static string Truncate(string text, int limit) =>
        text.Length > limit ? text[..limit] : text;

    /// <summary>
    /// Asks whether this really is the item meant. Mirrors verify().
    ///
    /// The full stop the description ends in becomes a question mark, so the
    /// question reads as one sentence.
    /// </summary>
    public bool Verify(string prompt, int slot)
    {
        string description = _game.Names.Describe(Pack[slot], withArticle: true);

        if (description.Length > 0)
        {
            description = description[..^1] + "?";
        }

        return _display.GetCheck(prompt + " " + description);
    }

    // ------------------------------------------------------ the item prompt

    /// <summary>
    /// Asks which item, and gives back the slot. Mirrors get_item().
    ///
    /// A range above <see cref="Inventory.WieldSlot"/> means the whole of what
    /// the player has, pack and equipment both, and "/" swaps between the two
    /// lists. Below it the range is a run of pack slots and there is nothing to
    /// swap to. A capital letter asks for confirmation; a digit picks by
    /// inscription rather than by letter.
    /// </summary>
    /// <param name="message">
    /// What to say when the answer is out of range, or nothing to just ring the
    /// bell - which is how a prompt says "not that kind of thing".
    /// </param>
    /// <returns>The slot chosen, or nothing when the player backed out.</returns>
    public int? GetItem(
        string prompt, int first, int last, bool[]? mask = null, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        int chosen = 0;
        bool picked = false;
        bool redrawn = false;

        // Which list is on show: 1 the pack, 0 the equipment, -1 finished.
        int showing = 1;
        bool full = last > Inventory.WieldSlot;

        if (full)
        {
            // An empty pack starts on the equipment instead, since there is
            // nothing else to show.
            if (Pack.Count == 0)
            {
                showing = 0;
                last = Pack.EquipmentCount - 1;
            }
            else
            {
                last = Pack.Count - 1;
            }
        }

        if (Pack.Count == 0 && !(full && Pack.EquipmentCount > 0))
        {
            _display.Print("You are not carrying anything.", 0, 0);
            return null;
        }

        do
        {
            if (redrawn)
            {
                if (showing > 0)
                {
                    ShowInventory(first, last, weight: false, 80, mask);
                }
                else
                {
                    ShowEquipment(weight: false, 80);
                }
            }

            string header = full
                ? "(" + (showing > 0 ? "Inven" : "Equip") + ": "
                    + (char)(first + 'a') + "-" + (char)(last + 'a') + ","
                    + (showing > 0 ? " 0-9," : string.Empty)
                    + (redrawn ? string.Empty : " * to see,")
                    + " / for " + (showing > 0 ? "Equip" : "Inven") + ", or ESC) " + prompt
                : "(Items " + (char)(first + 'a') + "-" + (char)(last + 'a') + ","
                    + (showing > 0 ? " 0-9," : string.Empty)
                    + (redrawn ? string.Empty : " * for inventory list,")
                    + " ESC to exit) " + prompt;

            bool asked = false;
            _display.Print(header, 0, 0);

            do
            {
                char which = _display.ReadKey();

                if (which == Keys.Escape)
                {
                    asked = true;
                    _loop.FreeTurn = true;
                    showing = -1;
                    break;
                }

                if (which == '/')
                {
                    if (full)
                    {
                        SwapLists(ref showing, ref last, ref asked, redrawn, header);
                    }

                    break;
                }

                if (which == '*')
                {
                    if (!redrawn)
                    {
                        asked = true;
                        _display.SaveScreen();
                        redrawn = true;
                    }

                    break;
                }

                if (which is >= '0' and <= '9' && showing != 0)
                {
                    // Picked by inscription rather than by letter, which is
                    // what inscribing a thing with a digit is for.
                    chosen = -1;

                    for (int m = first; m < Inventory.WieldSlot; m++)
                    {
                        if (Pack[m].Inscription == which.ToString(CultureInfo.InvariantCulture))
                        {
                            chosen = m;
                            break;
                        }
                    }
                }
                else if (char.IsUpper(which))
                {
                    chosen = which - 'A';
                }
                else
                {
                    chosen = which - 'a';
                }

                if (chosen >= first && chosen <= last
                    && (mask is null || mask[chosen]))
                {
                    if (showing == 0)
                    {
                        // The letter counted over the slots in use, so the slot
                        // itself has to be counted back out.
                        int wanted = chosen;
                        int slot = Inventory.WieldSlot - 1;

                        do
                        {
                            do
                            {
                                slot++;
                            }
                            while (Pack[slot].TVal == ItemCategory.Nothing);

                            wanted--;
                        }
                        while (wanted >= 0);

                        chosen = slot;
                    }

                    if (char.IsUpper(which) && !Verify("Try", chosen))
                    {
                        asked = true;
                        _loop.FreeTurn = true;
                        showing = -1;
                        break;
                    }

                    asked = true;
                    picked = true;
                    showing = -1;
                }
                else if (message is not null)
                {
                    _display.MessagePrint(message);

                    // Said rather than rung, and the question is put again.
                    asked = true;
                }
                else
                {
                    _display.Bell();
                }

                break;
            }
            while (!asked);
        }
        while (showing >= 0);

        if (redrawn)
        {
            _display.RestoreScreen();
        }

        _display.EraseLine(Display.MessageLine, 0);

        return picked ? chosen : null;
    }

    /// <summary>
    /// Swaps the prompt between the pack and the equipment. Refuses when the
    /// other list is empty, and says so with a pause rather than a message,
    /// since the prompt itself is on the message line.
    /// </summary>
    private void SwapLists(
        ref int showing, ref int last, ref bool asked, bool redrawn, string header)
    {
        if (showing > 0)
        {
            if (Pack.EquipmentCount == 0)
            {
                _display.Print("But you're not using anything -more-", 0, 0);
                _display.ReadKey();
            }
            else
            {
                showing = 0;
                asked = true;
                EraseBelow(redrawn, Pack.EquipmentCount, Pack.Count);
                last = Pack.EquipmentCount - 1;
            }

            _display.Print(header, 0, 0);
        }
        else
        {
            if (Pack.Count == 0)
            {
                _display.Print("But you're not carrying anything -more-", 0, 0);
                _display.ReadKey();
            }
            else
            {
                showing = 1;
                asked = true;
                EraseBelow(redrawn, Pack.Count, Pack.EquipmentCount);
                last = Pack.Count - 1;
            }
        }
    }

    /// <summary>
    /// Rubs out the lines the shorter of the two lists leaves behind, so the
    /// tail of the longer one does not stay on the screen under it.
    /// </summary>
    private void EraseBelow(bool redrawn, int from, int to)
    {
        if (!redrawn)
        {
            return;
        }

        for (int line = from; line < to;)
        {
            line++;
            _display.EraseLine(line, 0);
        }
    }
}
