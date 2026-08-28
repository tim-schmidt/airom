// Ported from the endgame half of Umoria 5.6 source/death.c - date,
// center_string, print_tomb, total_points, kingly and exit_game.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// What happens when the game ends.
///
/// A dead character gets a gravestone with their name on it and an offer to
/// write the whole character out to a file, and then goes into the score table.
/// A character who won gets crowned first: made king, given a fortune, and
/// killed off by old age, so that the tomb reads properly.
/// </summary>
public class Death
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Death(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    private Player Player => _game.Player;

    /// <summary>
    /// Today's date, in the form the gravestone carries it. Mirrors date(),
    /// which takes the first ten characters of ctime().
    /// </summary>
    protected virtual string Today() =>
        DateTime.Now.ToString("ddd MMM d", CultureInfo.InvariantCulture).PadRight(10);

    /// <summary>
    /// Centres a string in thirty-one columns. Mirrors center_string(), padding
    /// unevenly when it will not divide - the extra space goes on the right.
    /// </summary>
    public static string Centre(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int left = 15 - (text.Length / 2);
        int right = 31 - text.Length - left;

        return new string(' ', Math.Max(left, 0)) + text
            + new string(' ', Math.Max(right, 0));
    }

    /// <summary>
    /// What the character was worth. Mirrors total_points().
    ///
    /// Depth counts twice over - once for the deepest ever reached and once for
    /// where they died - and everything carried is counted at what a shop would
    /// pay. A score never falls, so a character who is saved and played on
    /// keeps whatever they had.
    /// </summary>
    public int TotalPoints()
    {
        int total = Player.MaxExperience + (100 * Player.MaxDungeonLevel);
        total += Player.Gold / 100;

        for (int i = 0; i < Inventory.Size; i++)
        {
            total += _game.Stores.ItemValue(_game.Inventory[i]);
        }

        total += _game.DungeonLevel * 50;

        return Math.Max(total, _game.MaxScore);
    }

    /// <summary>
    /// Crowns a winner. Mirrors kingly().
    ///
    /// The character is taken out of the dungeon, given a level beyond the
    /// last, a quarter of a million in gold and five million in experience, and
    /// then dies of old age - which is the only way to leave the game having
    /// won it.
    /// </summary>
    public void Crown()
    {
        _game.DungeonLevel = 0;
        _game.DiedFrom = "Ripe Old Age";

        _loop.Spells.RestoreLevel();

        Player.Level += Player.MaxLevel;
        Player.Gold += 250000;
        Player.MaxExperience += 5000000;
        Player.Experience = Player.MaxExperience;

        _display.ClearScreen();
        _display.PutBuffer("#", 1, 34);
        _display.PutBuffer("#####", 2, 32);
        _display.PutBuffer("#", 3, 34);
        _display.PutBuffer(",,,  $$$  ,,,", 4, 28);
        _display.PutBuffer(",,=$   \"$$$$$\"   $=,,", 5, 24);
        _display.PutBuffer(",$$        $$$        $$,", 6, 22);
        _display.PutBuffer("*>         <*>         <*", 7, 22);
        _display.PutBuffer("$$         $$$         $$", 8, 22);
        _display.PutBuffer("\"$$        $$$        $$\"", 9, 22);
        _display.PutBuffer("\"$$       $$$       $$\"", 10, 23);

        const string Band = "*#########*#########*";
        _display.PutBuffer(Band, 11, 24);
        _display.PutBuffer(Band, 12, 24);

        _display.PutBuffer("Veni, Vidi, Vici!", 15, 26);
        _display.PutBuffer("I came, I saw, I conquered!", 16, 21);

        _display.PutBuffer(
            Player.Male ? "All Hail the Mighty King!" : "All Hail the Mighty Queen!",
            17, 22);

        _display.FlushInput();
        _display.PauseLine(23);
    }

    /// <summary>
    /// The gravestone, and the offer to keep a record. Mirrors print_tomb().
    ///
    /// One prompt does two jobs: a file name writes the character out, an empty
    /// answer shows it on the screen instead, and an escape leaves without
    /// either. A file that cannot be written puts the whole thing up again.
    /// </summary>
    public void PrintTomb()
    {
        using IDisposable centred = _display.Centred();

        while (true)
        {
            DrawTomb();
            _display.FlushInput();

            _display.PutBuffer(
                "(ESC to abort, return to print on screen, or file name)", 23, 0);

            _display.PutBuffer("Character record?", 22, 0);

            if (!_display.GetString(22, 18, 60, out string name))
            {
                return;
            }

            // Everything is identified before it is written down: there is
            // nothing left to keep secret from a dead character.
            for (int i = 0; i < Inventory.Size; i++)
            {
                _game.Knowledge.LearnKind(_game.Inventory[i]);
                _game.Knowledge.LearnEnchantment(_game.Inventory[i]);
            }

            _loop.Equipment.Recalculate();

            if (name.Length == 0)
            {
                ShowRecord();
                return;
            }

            if (_loop.CharacterFile.Write(name))
            {
                return;
            }
        }
    }

    /// <summary>The stone itself.</summary>
    private void DrawTomb()
    {
        _display.ClearScreen();

        _display.PutBuffer("_______________________", 1, 15);
        _display.PutBuffer("/", 2, 14);
        _display.PutBuffer("\\         ___", 2, 38);
        _display.PutBuffer("/", 3, 13);
        _display.PutBuffer("\\ ___   /   \\      ___", 3, 39);
        _display.PutBuffer("/            RIP            \\   \\  :   :     /   \\", 4, 12);
        _display.PutBuffer("/", 5, 11);
        _display.PutBuffer("\\  : _;,,,;_    :   :", 5, 41);

        _display.PutBuffer("/" + Centre(Player.Name) + "\\,;_          _;,,,;_", 6, 10);
        _display.PutBuffer("|               the               |   ___", 7, 9);

        string rank = _game.TotalWinnerCrowned
            ? "Magnificent"
            : Display.TitleFor(Player);

        _display.PutBuffer("| " + Centre(rank) + " |  /   \\", 8, 9);
        _display.PutBuffer("|", 9, 9);
        _display.PutBuffer("|  :   :", 9, 43);

        string title = !_game.TotalWinnerCrowned
            ? GameTables.Classes[Player.Class].Title
            : Player.Male ? "*King*" : "*Queen*";

        _display.PutBuffer("| " + Centre(title) + " | _;,,,;_   ____", 10, 9);

        _display.PutBuffer(
            "| " + Centre("Level : " + N(Player.Level)) + " |          /    \\", 11, 9);

        _display.PutBuffer(
            "| " + Centre(N(Player.Experience) + " Exp") + " |          :    :", 12, 9);

        _display.PutBuffer(
            "| " + Centre(N(Player.Gold) + " Au") + " |          :    :", 13, 9);

        _display.PutBuffer(
            "| " + Centre("Died on Level : " + N(_game.DungeonLevel))
            + " |         _;,,,,;_", 14, 9);

        _display.PutBuffer("|            killed by            |", 15, 9);

        // A trailing stop, which is put on for the stone and taken off again.
        _display.PutBuffer("| " + Centre(_game.DiedFrom + ".") + " |", 16, 9);
        _display.PutBuffer("| " + Centre(Today()) + " |", 17, 9);

        _display.PutBuffer("*|   *     *     *    *   *     *  | *", 18, 8);
        _display.PutBuffer(
            "________)/\\\\_)_/___(\\/___(//_\\)/_\\//__\\\\(/_|_)_______", 19, 0);
    }

    /// <summary>The character sheet, and then what they were carrying.</summary>
    private void ShowRecord()
    {
        _display.ClearScreen();
        _loop.CharacterSheet.DisplayAll();

        _display.PutBuffer("Type ESC to skip the inventory:", 23, 0);

        if (_display.ReadKey() == Keys.Escape)
        {
            return;
        }

        _display.ClearScreen();
        _display.MessagePrint("You are using:");
        _loop.InventoryScreen.ShowEquipment(weight: true, 0);
        _display.MessagePrint(null);

        _display.MessagePrint("You are carrying:");
        _display.ClearFrom(1);
        _loop.InventoryScreen.ShowInventory(
            0, _game.Inventory.Count - 1, weight: true, 0, null);

        _display.MessagePrint(null);
    }

    /// <summary>
    /// The whole end of the game. Mirrors exit_game(), less the parts that
    /// belong to the terminal and the process.
    /// </summary>
    public void ExitGame()
    {
        _display.MessagePrint(null);
        _display.FlushInput();

        // Can't interrupt or suspend: the original calls nosignals() here, so
        // a Ctrl-C over the tomb or the score table goes back to being a key.
        _display.Interrupted = null;

        // A saved game sets the turn to -1, which is what stops the tomb being
        // printed for a character who merely stopped playing.
        if (_game.Turn >= 0)
        {
            if (_game.TotalWinner)
            {
                _game.TotalWinnerCrowned = true;
                Crown();
            }

            PrintTomb();
        }

        if (!_game.CharacterGenerated)
        {
            return;
        }

        // A character who died is written out where they lived, over whatever
        // they last saved. This is what makes death permanent: the file left
        // behind holds a dead character, so the next game reads it for what it
        // learned and rolls someone new. Without it the last living save is
        // still sitting there, and the dead walk again.
        if (!_game.CharacterSaved)
        {
            _loop.SaveFile.SaveWithRetry();
        }

        // Cleared before the score is written, which is a strange thing to do
        // until you notice it stops a end-of-input inside the score table
        // calling this all over again.
        _game.CharacterSaved = false;

        var scores = new ScoreFile(_game, _display);
        scores.Record(TotalPoints(), _game.DiedFrom);
        scores.Display(playerOnly: true);

        using IDisposable centred = _display.Centred();
        _display.EraseLine(23, 0);
    }

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}
