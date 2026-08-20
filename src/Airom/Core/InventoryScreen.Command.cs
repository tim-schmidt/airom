// Ported from inven_screen() and inven_command() in Umoria 5.6 source/moria1.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

public partial class InventoryScreen
{
    /// <summary>
    /// Draws one of the lists, or rubs out what the last one left. Mirrors
    /// inven_screen().
    ///
    /// Nothing is drawn when the list asked for is already up, which is what
    /// makes the mode cheap to sit in. The bottom of the longest list drawn so
    /// far is remembered, so a shorter one that follows can rub out the lines
    /// below it.
    /// </summary>
    private void DrawScreen(Screen wanted)
    {
        if (wanted == _screen)
        {
            return;
        }

        _screen = wanted;
        int line;

        switch (wanted)
        {
            case Screen.Help:
                if (_left > 52)
                {
                    _left = 52;
                }

                _display.Print("  ESC: exit", 1, _left);
                _display.Print("  w  : wear or wield object", 2, _left);
                _display.Print("  t  : take off item", 3, _left);
                _display.Print("  d  : drop object", 4, _left);
                _display.Print("  x  : exchange weapons", 5, _left);
                _display.Print("  i  : inventory of pack", 6, _left);
                _display.Print("  e  : list used equipment", 7, _left);
                line = 7;
                break;

            case Screen.Inventory:
                _left = ShowInventory(0, Pack.Count - 1, _game.ShowWeights, _left, null);
                line = Pack.Count;
                break;

            case Screen.Wearable:
                _left = ShowInventory(_wearLow, _wearHigh, _game.ShowWeights, _left, null);
                line = _wearHigh - _wearLow + 1;
                break;

            case Screen.Equipment:
                _left = ShowEquipment(_game.ShowWeights, _left);
                line = Pack.EquipmentCount;
                break;

            default:
                line = 0;
                break;
        }

        if (line >= _base)
        {
            _base = line + 1;
            _display.EraseLine(_base, _left);
        }
        else
        {
            while (++line <= _base)
            {
                _display.EraseLine(line, _left);
            }
        }
    }

    /// <summary>
    /// The whole inventory mode: listing, wearing, taking off, dropping and
    /// swapping weapons. Mirrors inven_command().
    ///
    /// It returns as soon as something costs a turn, leaving
    /// <see cref="ContinuingCommand"/> set so the caller brings it back next
    /// turn with the screen still up. That is how the rest of the game gets its
    /// turns while the player is still standing in front of their pack.
    /// </summary>
    public void Command(char command)
    {
        _loop.FreeTurn = true;
        _display.SaveScreen();

        if (ContinuingCommand is not null)
        {
            // If the screen was flushed while the world took its turn, it has
            // to be drawn again - and the player is asked first, since what
            // flushed it may be something they want to look at.
            if (_display.ScreenChanged)
            {
                if (command == ' '
                    || !_display.GetCheck("Continuing with inventory command?"))
                {
                    ContinuingCommand = null;
                    return;
                }

                _left = 50;
                _base = 0;
            }

            Screen showing = _screen;
            _screen = Screen.Wrong;
            DrawScreen(showing);
        }
        else
        {
            _left = 50;
            _base = 0;

            // Nothing drawn, which is what makes a command that selects nothing
            // leave the mode again straight away.
            _screen = Screen.Blank;
        }

        char which = 'z';

        do
        {
            if (char.IsUpper(command))
            {
                command = char.ToLower(command, CultureInfo.InvariantCulture);
            }

            bool selecting = HandleCommand(ref command);

            // Cleared here rather than at the top, so that the messages above
            // can tell whether this is a fresh command or a resumed one.
            ContinuingCommand = null;

            which = 'z';

            while (selecting && _loop.FreeTurn)
            {
                selecting = Select(ref command, ref which);
            }

            if (which == Keys.Escape || _screen == Screen.Blank)
            {
                command = Keys.Escape;
            }
            else if (!_loop.FreeTurn)
            {
                // Saved so that the mode can be picked up next turn.
                ContinuingCommand = selecting ? command : ' ';

                // The last message is flushed before the screen is cleared, and
                // the flag is cleared afterwards so that anything the world does
                // in the meantime is noticed.
                _display.MessagePrint(null);
                _display.ScreenChanged = false;
                command = Keys.Escape;
            }
            else
            {
                PrintHeader();
                _display.EraseLine(_base, _left);
                _display.PutBuffer("e/i/t/w/x/d/?/ESC:", _base, 60);
                command = _display.ReadKey();
                _display.EraseLine(_base, _left);
            }
        }
        while (command != Keys.Escape);

        if (_screen != Screen.Blank)
        {
            _display.RestoreScreen();
        }

        _loop.Equipment.Recalculate();
    }

    /// <summary>
    /// Does what a single command key asks, and says whether an item still has
    /// to be picked to finish it.
    /// </summary>
    private bool HandleCommand(ref char command)
    {
        switch (command)
        {
            case 'i':
                if (Pack.Count == 0)
                {
                    _display.MessagePrint("You are not carrying anything.");
                }
                else
                {
                    DrawScreen(Screen.Inventory);
                }

                return false;

            case 'e':
                if (Pack.EquipmentCount == 0)
                {
                    _display.MessagePrint("You are not using any equipment.");
                }
                else
                {
                    DrawScreen(Screen.Equipment);
                }

                return false;

            case 't':
                if (Pack.EquipmentCount == 0)
                {
                    _display.MessagePrint("You are not using any equipment.");
                    return false;
                }

                // Nothing is said when the mode is being resumed: having just
                // taken something off, being told the pack is full reads as a
                // complaint about the wrong thing.
                if (Pack.Count >= Inventory.WieldSlot && ContinuingCommand is null)
                {
                    _display.MessagePrint("You will have to drop something first.");
                    return false;
                }

                if (_screen != Screen.Blank)
                {
                    DrawScreen(Screen.Equipment);
                }

                return true;

            case 'd':
                if (Pack.Count == 0 && Pack.EquipmentCount == 0)
                {
                    _display.MessagePrint("But you're not carrying anything.");
                    return false;
                }

                if (_game.Cave[_game.CharacterRow, _game.CharacterColumn].ObjectIndex != 0)
                {
                    _display.MessagePrint("There's no room to drop anything here.");
                    return false;
                }

                if ((_screen == Screen.Equipment && Pack.EquipmentCount > 0)
                    || Pack.Count == 0)
                {
                    if (_screen != Screen.Blank)
                    {
                        DrawScreen(Screen.Equipment);
                    }

                    // "Throw off": take it off and drop it in one go.
                    command = 'r';
                }
                else if (_screen != Screen.Blank)
                {
                    DrawScreen(Screen.Inventory);
                }

                return true;

            case 'w':
                FindWearable();

                if (_wearLow > _wearHigh)
                {
                    _display.MessagePrint("You have nothing to wear or wield.");
                    return false;
                }

                if (_screen != Screen.Blank && _screen != Screen.Inventory)
                {
                    DrawScreen(Screen.Wearable);
                }

                return true;

            case 'x':
                SwapWeapons();
                return false;

            case ' ':
                // A dummy command, which brings the screen back and nothing more.
                return false;

            case '?':
                DrawScreen(Screen.Help);
                return false;

            default:
                _display.Bell();
                return false;
        }
    }

    /// <summary>
    /// Finds the run of pack slots holding something wearable. The pack is
    /// sorted by category, so everything that can be worn is in one block.
    /// </summary>
    private void FindWearable()
    {
        _wearLow = 0;

        while (_wearLow < Pack.Count && Pack[_wearLow].TVal > ItemCategory.MaxWear)
        {
            _wearLow++;
        }

        _wearHigh = _wearLow;

        while (_wearHigh < Pack.Count && Pack[_wearHigh].TVal >= ItemCategory.MinWear)
        {
            _wearHigh++;
        }

        _wearHigh--;
    }

    /// <summary>
    /// Swaps the wielded weapon with the spare. Mirrors the "x" branch.
    ///
    /// The spare grants nothing while it is spare, so the bonuses are taken off
    /// one and put on the other rather than simply moved.
    /// </summary>
    private void SwapWeapons()
    {
        InvenType wielded = Pack[Inventory.WieldSlot];
        InvenType spare = Pack[Inventory.AuxiliarySlot];

        if (wielded.TVal == ItemCategory.Nothing && spare.TVal == ItemCategory.Nothing)
        {
            _display.MessagePrint("But you are wielding no weapons.");
            return;
        }

        if ((wielded.Flags & ItemFlags.Cursed) != 0)
        {
            _display.MessagePrint(
                "The " + _game.Names.Describe(wielded, withArticle: false)
                + " you are wielding appears to be cursed.");
            return;
        }

        _loop.FreeTurn = false;

        var held = new InvenType();
        held.CopyStateFrom(spare);
        spare.CopyStateFrom(wielded);
        wielded.CopyStateFrom(held);

        if (_screen == Screen.Equipment)
        {
            _left = ShowEquipment(_game.ShowWeights, _left);
        }

        _loop.Equipment.ApplyItem(spare, -1);
        _loop.Equipment.ApplyItem(wielded, 1);

        _display.MessagePrint(wielded.TVal != ItemCategory.Nothing
            ? "Primary weapon   : " + _game.Names.Describe(wielded, withArticle: true)
            : "No primary weapon.");

        // A new weapon, so whether the last one was too heavy says nothing
        // about this one.
        Pack.WeaponTooHeavy = false;
        _loop.Equipment.CheckStrength();
    }

    /// <summary>
    /// Asks which item the current command applies to, and does it. Mirrors the
    /// inner selecting loop.
    /// </summary>
    /// <returns>Whether to keep asking.</returns>
    private bool Select(ref char command, ref char which)
    {
        (int from, int to, string prompt, string swap) = SelectionRange(command);

        if (from > to)
        {
            return false;
        }

        string list = _screen == Screen.Blank ? ", * to list" : string.Empty;
        string digits = command is 'w' or 'd' ? ", 0-9" : string.Empty;

        string header = "(" + (char)(from + 'a') + "-" + (char)(to + 'a')
            + list + swap + digits
            + ", space to break, ESC to exit) " + prompt + " which one?";

        if (!_display.GetCommand(header, out which))
        {
            which = Keys.Escape;
            return false;
        }

        if (which is ' ' or '*')
        {
            if (command is 't' or 'r')
            {
                DrawScreen(Screen.Equipment);
            }
            else if (command == 'w' && _screen != Screen.Inventory)
            {
                DrawScreen(Screen.Wearable);
            }
            else
            {
                DrawScreen(Screen.Inventory);
            }

            return which != ' ';
        }

        if (which == '/' && swap.Length > 0)
        {
            command = command == 'd' ? 'r' : 'd';

            if (_screen == Screen.Equipment)
            {
                DrawScreen(Screen.Inventory);
            }
            else if (_screen == Screen.Inventory)
            {
                DrawScreen(Screen.Equipment);
            }

            return true;
        }

        int item = PickItem(which, command, from, to);

        if (item < from || item > to)
        {
            _display.Bell();
            return true;
        }

        bool selecting = command switch
        {
            'r' or 't' => TakeOffOrThrow(ref command, item, which, prompt),
            'w' => Wear(item, which, prompt),
            _ => Drop(item, which, prompt),
        };

        // A command that took a turn while nothing was drawn ends the mode:
        // there is no list to come back to.
        if (!_loop.FreeTurn && _screen == Screen.Blank)
        {
            selecting = false;
        }

        return selecting;
    }

    /// <summary>What the current command is asking for, and how it says so.</summary>
    private (int From, int To, string Prompt, string Swap) SelectionRange(char command)
    {
        if (command == 'w')
        {
            return (_wearLow, _wearHigh, "Wear/Wield", string.Empty);
        }

        if (command == 'd')
        {
            return (0, Pack.Count - 1, "Drop",
                    Pack.EquipmentCount > 0 ? ", / for Equip" : string.Empty);
        }

        if (command == 't')
        {
            return (0, Pack.EquipmentCount - 1, "Take off", string.Empty);
        }

        return (0, Pack.EquipmentCount - 1, "Throw off",
                Pack.Count > 0 ? ", / for Inven" : string.Empty);
    }

    /// <summary>
    /// Turns the key pressed into a pack slot. A digit matches an inscription
    /// rather than a letter, which is only offered where the list is the pack.
    /// </summary>
    private int PickItem(char which, char command, int from, int to)
    {
        if (which is >= '0' and <= '9' && command != 'r' && command != 't')
        {
            for (int m = from; m <= to; m++)
            {
                if (Pack[m].Inscription == which.ToString(CultureInfo.InvariantCulture))
                {
                    return m;
                }
            }

            return -1;
        }

        return char.IsUpper(which) ? which - 'A' : which - 'a';
    }

    /// <summary>
    /// Takes something off, or throws it off - which is taking it off and
    /// dropping it in the same move. Mirrors the "t" and "r" branches.
    /// </summary>
    private bool TakeOffOrThrow(ref char command, int item, char which, string prompt)
    {
        // The letter counted over the slots in use, so the slot itself has to
        // be counted back out.
        int remaining = item;
        int slot = Inventory.WieldSlot - 1;

        do
        {
            slot++;

            if (Pack[slot].TVal != ItemCategory.Nothing)
            {
                remaining--;
            }
        }
        while (remaining >= 0);

        if (char.IsUpper(which) && !Verify(prompt, slot))
        {
            return true;
        }

        if ((Pack[slot].Flags & ItemFlags.Cursed) != 0)
        {
            _display.MessagePrint("Hmmm, it seems to be cursed.");
            return true;
        }

        if (command == 't' && !Pack.HasRoomFor(Pack[slot]))
        {
            if (_game.Cave[_game.CharacterRow, _game.CharacterColumn].ObjectIndex != 0)
            {
                _display.MessagePrint("You can't carry it.");
                return true;
            }

            if (_display.GetCheck("You can't carry it.  Drop it?"))
            {
                command = 'r';
            }
            else
            {
                return true;
            }
        }

        if (command == 'r')
        {
            _loop.Equipment.Drop(slot, true);

            // A safety measure: with nothing carried at all, the weight cannot
            // be anything but nothing.
            if (Pack.Count == 0 && Pack.EquipmentCount == 0)
            {
                Pack.ClearWeight();
            }
        }
        else
        {
            int carried = Pack.Carry(Pack[slot]);
            _loop.Equipment.TakeOff(slot, carried);
        }

        _loop.Equipment.CheckStrength();
        _loop.FreeTurn = false;

        return command != 'r';
    }

    /// <summary>
    /// Puts something on. Mirrors the "w" branch, which goes to some trouble
    /// over what is already in the slot.
    /// </summary>
    private bool Wear(int item, char which, string prompt)
    {
        if (char.IsUpper(which) && !Verify(prompt, item))
        {
            return true;
        }

        int? chosen = SlotFor(item);

        if (chosen is not int slot)
        {
            return true;
        }

        InvenType worn = Pack[slot];

        if (worn.TVal != ItemCategory.Nothing)
        {
            if ((worn.Flags & ItemFlags.Cursed) != 0)
            {
                // FAITHFUL QUIRK: the head is the slot that says "wielding"
                // rather than "wearing", which reads backwards and is the
                // original's own slip.
                _display.MessagePrint(
                    "The " + _game.Names.Describe(worn, withArticle: false)
                    + " you are "
                    + (slot == Inventory.HeadSlot ? "wielding " : "wearing ")
                    + "appears to be cursed.");
                return true;
            }

            if (Pack[item].SubVal == ItemCategory.GroupMin
                && Pack[item].Number > 1
                && !Pack.HasRoomFor(worn))
            {
                // Carrying several torches and wielding one leaves the rest
                // needing a slot of their own.
                _display.MessagePrint("You will have to drop something first.");
                return true;
            }
        }

        _loop.FreeTurn = false;

        // First the new item comes out of the pack.
        var moving = new InvenType();
        moving.CopyStateFrom(Pack[item]);

        _wearHigh--;

        // One torch out of a bundle, rather than the bundle.
        if (moving.Number > 1 && moving.SubVal <= ItemCategory.SingleStackMax)
        {
            moving.Number = 1;
            _wearHigh++;
        }

        Pack.EquipWeightOnly(moving.Weight * moving.Number);
        Pack.Destroy(item);

        // Then whatever was in the slot goes into the pack.
        if (worn.TVal != ItemCategory.Nothing)
        {
            int before = Pack.Count;
            int carried = Pack.Carry(worn);

            // Only a slot of its own moves the end of the wearable run.
            if (Pack.Count != before)
            {
                _wearHigh++;
            }

            _loop.Equipment.TakeOff(slot, carried);
        }

        // And last the new item goes on.
        worn.CopyStateFrom(moving);
        Pack.EquipmentCount++;
        _loop.Equipment.ApplyItem(worn, 1);

        string verb = slot == Inventory.WieldSlot ? "You are wielding"
            : slot == Inventory.LightSlot ? "Your light source is"
            : "You are wearing";

        // The letter it landed on, counted over the slots in use.
        int letter = 0;

        for (int i = Inventory.WieldSlot; i != slot; i++)
        {
            if (Pack[i].TVal != ItemCategory.Nothing)
            {
                letter++;
            }
        }

        _display.MessagePrint(
            verb + " " + _game.Names.Describe(worn, withArticle: true)
            + " (" + (char)('a' + letter) + ")");

        if (slot == Inventory.WieldSlot)
        {
            Pack.WeaponTooHeavy = false;
        }

        _loop.Equipment.CheckStrength();

        if ((worn.Flags & ItemFlags.Cursed) != 0)
        {
            _display.MessagePrint("Oops! It feels deathly cold!");
            ItemKnowledge.AddInscription(worn, Identification.Damned);

            // Forces a price of nothing, even while it is unidentified.
            worn.Cost = -1;
        }

        return true;
    }

    /// <summary>
    /// Which slot a thing goes in. A ring is the only thing with two choices,
    /// and both taken means asking which hand.
    /// </summary>
    /// <returns>The slot, or nothing when the player backed out.</returns>
    private int? SlotFor(int item)
    {
        switch (Pack[item].TVal)
        {
            case ItemCategory.SlingAmmo:
            case ItemCategory.Bolt:
            case ItemCategory.Arrow:
            case ItemCategory.Bow:
            case ItemCategory.Hafted:
            case ItemCategory.Polearm:
            case ItemCategory.Sword:
            case ItemCategory.Digging:
            case ItemCategory.Spike:
                return Inventory.WieldSlot;

            case ItemCategory.Light:
                return Inventory.LightSlot;

            case ItemCategory.Boots:
                return Inventory.FeetSlot;

            case ItemCategory.Gloves:
                return Inventory.HandsSlot;

            case ItemCategory.Cloak:
                return Inventory.OuterSlot;

            case ItemCategory.Helm:
                return Inventory.HeadSlot;

            case ItemCategory.Shield:
                return Inventory.ArmSlot;

            case ItemCategory.HardArmor:
            case ItemCategory.SoftArmor:
                return Inventory.BodySlot;

            case ItemCategory.Amulet:
                return Inventory.NeckSlot;

            case ItemCategory.Ring:
                return RingSlot();

            default:
                _display.MessagePrint("IMPOSSIBLE: I don't see how you can use that.");
                return null;
        }
    }

    /// <summary>
    /// Which hand a ring goes on. An empty hand is used without asking; with
    /// both full the player picks, and is asked to confirm replacing what is
    /// already there.
    /// </summary>
    private int? RingSlot()
    {
        if (Pack[Inventory.RightRingSlot].TVal == ItemCategory.Nothing)
        {
            return Inventory.RightRingSlot;
        }

        if (Pack[Inventory.LeftRingSlot].TVal == ItemCategory.Nothing)
        {
            return Inventory.LeftRingSlot;
        }

        while (true)
        {
            if (!_display.GetCommand("Put ring on which hand (l/r/L/R)?", out char hand))
            {
                return null;
            }

            // A small letter goes on without asking; a capital confirms first.
            if (hand == 'l')
            {
                return Inventory.LeftRingSlot;
            }

            if (hand == 'r')
            {
                return Inventory.RightRingSlot;
            }

            int slot = hand switch
            {
                'L' => Inventory.LeftRingSlot,
                'R' => Inventory.RightRingSlot,
                _ => 0,
            };

            if (slot == 0)
            {
                _display.Bell();
                continue;
            }

            if (Verify("Replace", slot))
            {
                return slot;
            }
        }
    }

    /// <summary>
    /// Puts something down. Mirrors the "d" branch.
    ///
    /// A pile asks whether the whole of it goes, and that question is put with
    /// its own key rather than through get_check(), so an escape declines
    /// without dropping anything at all.
    /// </summary>
    private bool Drop(int item, char which, string prompt)
    {
        bool all = true;

        if (Pack[item].Number > 1)
        {
            string description = _game.Names.Describe(Pack[item], withArticle: true);

            if (description.Length > 0)
            {
                description = description[..^1] + "?";
            }

            _display.Print("Drop all " + description + " [y/n]", 0, 0);

            char answer = _display.ReadKey();

            if (answer != 'y' && answer != 'n')
            {
                if (answer != Keys.Escape)
                {
                    _display.Bell();
                }

                _display.EraseLine(Display.MessageLine, 0);
                return false;
            }

            all = answer == 'y';
        }
        else if (char.IsUpper(which) && !Verify(prompt, item))
        {
            return false;
        }

        _loop.FreeTurn = false;
        _loop.Equipment.Drop(item, all);
        _loop.Equipment.CheckStrength();

        // A safety measure: with nothing carried at all, the weight cannot be
        // anything but nothing.
        if (Pack.Count == 0 && Pack.EquipmentCount == 0)
        {
            Pack.ClearWeight();
        }

        return false;
    }

    /// <summary>
    /// What sits above the list. The inventory header is the one that carries
    /// the weight, since that is what the list is mostly about.
    /// </summary>
    private void PrintHeader()
    {
        string text;

        if (_screen == Screen.Inventory)
        {
            string carried = "You are carrying "
                + (Pack.Weight / 10).ToString(CultureInfo.InvariantCulture)
                + "." + (Pack.Weight % 10).ToString(CultureInfo.InvariantCulture)
                + " pounds. ";

            text = !_game.ShowWeights || Pack.Count == 0
                ? carried + "In your pack there is "
                    + (Pack.Count == 0 ? "nothing." : "-")
                : carried + "Your capacity is "
                    + (Pack.WeightLimit() / 10).ToString(CultureInfo.InvariantCulture)
                    + "." + (Pack.WeightLimit() % 10).ToString(CultureInfo.InvariantCulture)
                    + " pounds. In your pack is -";
        }
        else if (_screen == Screen.Wearable)
        {
            text = _wearHigh < _wearLow
                ? "You have nothing you could wield."
                : "You could wield -";
        }
        else if (_screen == Screen.Equipment)
        {
            text = Pack.EquipmentCount == 0
                ? "You are not using anything."
                : "You are using -";
        }
        else
        {
            text = "Allowed commands:";
        }

        _display.Print(text, 0, 0);
    }
}
