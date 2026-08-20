// Ported from the message and prompt half of Umoria 5.6 source/io.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Text;
using Airom.Data;

namespace Airom.Core;

/// <summary>Keys the game treats specially. Mirrors the constants in constant.h.</summary>
public static class Keys
{
    /// <summary>Umoria's ESCAPE, which is the real escape character outside VMS.</summary>
    public const char Escape = '';

    public const char Delete = '';

    /// <summary>Turns a letter into its control character. Mirrors CTRL(x).</summary>
    public static char Control(char letter) => (char)(letter & 0x1F);

    /// <summary>Redraw the screen. The one key inkey() handles itself.</summary>
    public static readonly char Redraw = Control('R');

    public static readonly char Backspace = Control('H');

    public static readonly char LineFeed = Control('J');

    public static readonly char Return = Control('M');
}

public sealed partial class Display
{
    /// <summary>How many messages are kept for review. Umoria's MAX_SAVE_MSG.</summary>
    public const int SavedMessageCount = 22;

    /// <summary>Longest message stored. Umoria's VTYPESIZ.</summary>
    private const int MessageLength = 80;

    private readonly string[] _oldMessages = new string[SavedMessageCount];
    private int _lastMessage;

    /// <summary>
    /// Whether a message is on screen and has not yet been acknowledged. The
    /// next message has to deal with it before overwriting.
    /// </summary>
    public bool MessageWaiting { get; private set; }

    /// <summary>
    /// Clears the waiting flag without printing. The command loop does this
    /// before reading a key, so a message the player has already had a chance to
    /// read does not force a -more- onto the next one.
    /// </summary>
    public bool MessageWaitingFlag
    {
        get => MessageWaiting;
        set => MessageWaiting = value;
    }

    /// <summary>Set when a message needs a keypress the caller should not swallow.</summary>
    public int CommandCount { get; set; }

    /// <summary>The messages kept for review, most recent last.</summary>
    public IReadOnlyList<string> RecentMessages => _oldMessages;

    /// <summary>Where the most recent message sits in the ring.</summary>
    public int LastMessageIndex => _lastMessage;

    /// <summary>
    /// Shows a message on the top line. Mirrors msg_print().
    ///
    /// Two messages in a row are combined onto one line when they both fit,
    /// which is why short notices run together. When they do not fit, the
    /// previous one gets a -more- and waits for a key, so nothing is lost by
    /// scrolling past.
    ///
    /// Passing null flushes whatever is showing without printing anything.
    /// </summary>
    public void MessagePrint(string? text)
    {
        bool combine = false;
        int oldLength = 0;

        if (MessageWaiting)
        {
            oldLength = _oldMessages[_lastMessage].Length + 1;
            int newLength = text?.Length ?? 0;

            if (text is null || newLength + oldLength + 2 >= 73)
            {
                // Keep the whole -more- visible even after a very long message.
                if (oldLength > 73)
                {
                    oldLength = 73;
                }

                PutBuffer(" -more-", MessageLine, oldLength);

                char key;
                do
                {
                    key = ReadKey();
                }
                while (key != ' ' && key != Keys.Escape && key != '\n' && key != '\r');
            }
            else
            {
                combine = true;
            }
        }

        if (!combine)
        {
            _screen.EraseLine(MessageLine, 0);
        }

        if (text is null)
        {
            MessageWaiting = false;
            return;
        }

        CommandCount = 0;
        MessageWaiting = true;

        if (combine)
        {
            PutBuffer(text, MessageLine, oldLength + 2);
            _oldMessages[_lastMessage] += "  " + text;
        }
        else
        {
            PutBuffer(text, MessageLine, 0);

            _lastMessage++;
            if (_lastMessage >= SavedMessageCount)
            {
                _lastMessage = 0;
            }

            _oldMessages[_lastMessage] =
                text.Length > MessageLength - 1 ? text[..(MessageLength - 1)] : text;
        }
    }

    /// <summary>
    /// Shows a message without disturbing a repeating command. Mirrors
    /// count_msg_print().
    /// </summary>
    public void CountMessagePrint(string text)
    {
        int saved = CommandCount;
        MessagePrint(text);
        CommandCount = saved;
    }

    /// <summary>
    /// Reads one key. Mirrors inkey().
    ///
    /// It flushes pending output first, so the player always sees the state they
    /// are answering about, and handles a redraw request itself rather than
    /// passing it on.
    /// </summary>
    public char ReadKey()
    {
        Refresh();
        CommandCount = 0;

        while (true)
        {
            char key = _screen.ReadKey();
            if (key != Keys.Redraw)
            {
                return key;
            }

            _screen.Refresh();
        }
    }

    /// <summary>Sends pending drawing to the screen. Mirrors put_qio().</summary>
    public void Refresh()
    {
        ScreenChanged = true;
        _screen.Refresh();
    }

    /// <summary>
    /// Set whenever the screen is repainted, so the inventory display knows to
    /// redraw itself. Mirrors Umoria's screen_change.
    /// </summary>
    public bool ScreenChanged { get; set; }

    /// <summary>
    /// Whether a key is waiting. Mirrors check_input(), which is what lets a
    /// keypress interrupt a rest or a run.
    /// </summary>
    public bool KeyAvailable => _screen.KeyAvailable;

    /// <summary>Discards type-ahead. Mirrors flush().</summary>
    public void FlushInput() => _screen.FlushInput();

    /// <summary>Rings the bell, unless the player has turned it off. Mirrors bell().</summary>
    public void Bell()
    {
        Refresh();

        if (_game.SoundEnabled)
        {
            _screen.Bell();
        }
    }

    /// <summary>
    /// Asks a yes or no question. Mirrors get_check().
    ///
    /// Spaces are ignored rather than taken as an answer, so a player mashing
    /// past a message does not accidentally decline. Anything that is not a "y"
    /// is a no.
    /// </summary>
    public bool GetCheck(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        Print(prompt, 0, 0);

        // The prompt goes right after the question, or at column 73 if the
        // question runs long.
        int column = Math.Min(prompt.Length, 73);
        PutBuffer(" [y/n]", 0, column);

        char answer;
        do
        {
            answer = ReadKey();
        }
        while (answer == ' ');

        EraseLine(0, 0);
        return answer is 'Y' or 'y';
    }

    /// <summary>
    /// Asks for a single command key. Mirrors get_com().
    /// </summary>
    /// <returns>False if the player pressed escape.</returns>
    public bool GetCommand(string? prompt, out char command)
    {
        if (prompt is not null)
        {
            Print(prompt, 0, 0);
        }

        command = ReadKey();
        bool answered = command != Keys.Escape;

        EraseLine(MessageLine, 0);
        return answered;
    }

    /// <summary>
    /// Reads a line of text, ended by return. Mirrors get_string().
    ///
    /// Backspace rubs out, escape abandons the whole thing, and anything
    /// unprintable or past the field width just rings the bell. Trailing spaces
    /// are trimmed on the way out.
    /// </summary>
    /// <returns>False if the player pressed escape.</returns>
    public bool GetString(int row, int column, int length, out string text)
    {
        text = string.Empty;

        // Clear the field, then work within it.
        _screen.Put(row, column, new string(' ', length));

        int startColumn = column;
        int endColumn = column + length - 1;
        if (endColumn > 79)
        {
            endColumn = 79;
        }

        var typed = new StringBuilder();
        bool done = false;
        bool abandoned = false;

        do
        {
            char key = ReadKey();

            if (key == Keys.Escape)
            {
                abandoned = true;
            }
            else if (key == Keys.LineFeed || key == Keys.Return)
            {
                done = true;
            }
            else if (key == Keys.Delete || key == Keys.Backspace)
            {
                if (column > startColumn)
                {
                    column--;
                    PutBuffer(" ", row, column);
                    MoveCursor(row, column);
                    if (typed.Length > 0)
                    {
                        typed.Length--;
                    }
                }
            }
            else if (char.IsControl(key) || column > endColumn)
            {
                Bell();
            }
            else
            {
                _screen.Put(row, column, key);
                typed.Append(key);
                column++;
            }
        }
        while (!done && !abandoned);

        if (abandoned)
        {
            return false;
        }

        text = typed.ToString().TrimEnd(' ');
        return true;
    }

    /// <summary>Waits for any key. Mirrors pause_line().</summary>
    public void PauseLine(int row)
    {
        Print("[Press any key to continue.]", row, 23);
        ReadKey();
        EraseLine(row, 0);
    }

    /// <summary>
    /// Waits for a key, offering to quit. Mirrors pause_exit().
    /// </summary>
    /// <returns>True if the player asked to quit.</returns>
    public bool PauseExit(int row)
    {
        Print("[Press any key to continue, or Q to exit.]", row, 10);
        char key = ReadKey();
        EraseLine(row, 0);
        return key == 'Q';
    }

    /// <summary>Snapshots the screen so an overlay can be drawn. Mirrors save_screen().</summary>
    public void SaveScreen() => _screen.SaveScreen();

    /// <summary>Puts back what <see cref="SaveScreen"/> captured. Mirrors restore_screen().</summary>
    public void RestoreScreen() => _screen.RestoreScreen();

    // ------------------------------------------------------------ whole map

    /// <summary>How many dungeon squares one map square stands for. Umoria's RATIO.</summary>
    private const int MapRatio = 3;

    /// <summary>
    /// Draws the whole level scaled down to one screen. Mirrors screen_map(),
    /// less its final wait for a key.
    ///
    /// Three squares by three collapse into one, so something has to win. The
    /// priorities decide what: the player over stairs, stairs over doors, doors
    /// over walls, walls over floor, and anything at all over the unknown - so
    /// the landmarks survive the shrinking and the blank space gives way.
    /// </summary>
    /// <returns>Where the player ended up on the scaled map, or (-1, -1).</returns>
    public (int Row, int Column) ScreenMap()
    {
        var priority = new Dictionary<char, int>
        {
            ['<'] = 5,
            ['>'] = 5,
            ['@'] = 10,
            ['#'] = -5,
            ['.'] = -10,
            ['\''] = -3,
            [' '] = -15,
        };

        int Priority(char symbol) => priority.TryGetValue(symbol, out int p) ? p : 0;

        int mapWidth = _game.Cave.Width / MapRatio;
        var row = new char[mapWidth];

        SaveScreen();
        ClearScreen();

        // Top border.
        _screen.Put(0, 0, '+');
        _screen.Put(0, 1, new string('-', mapWidth));
        _screen.Put(0, mapWidth + 1, '+');

        int lastMapRow = -1;
        int playerRow = -1;
        int playerColumn = -1;

        for (int y = 0; y < _game.Cave.Height; y++)
        {
            int mapRow = y / MapRatio;

            if (mapRow != lastMapRow)
            {
                if (lastMapRow >= 0)
                {
                    WriteMapRow(lastMapRow, row, mapWidth);
                }

                Array.Fill(row, ' ');
                lastMapRow = mapRow;
            }

            for (int x = 0; x < _game.Cave.Width; x++)
            {
                int mapColumn = x / MapRatio;
                char symbol = SymbolAt(y, x);

                if (Priority(row[mapColumn]) < Priority(symbol))
                {
                    row[mapColumn] = symbol;
                }

                if (row[mapColumn] == '@')
                {
                    // Both offset by one, to clear the border.
                    playerColumn = mapColumn + 1;
                    playerRow = mapRow + 1;
                }
            }
        }

        if (lastMapRow >= 0)
        {
            WriteMapRow(lastMapRow, row, mapWidth);
        }

        // Bottom border.
        _screen.Put(lastMapRow + 2, 0, '+');
        _screen.Put(lastMapRow + 2, 1, new string('-', mapWidth));
        _screen.Put(lastMapRow + 2, mapWidth + 1, '+');

        _screen.Put(23, 23, "Hit any key to continue");

        if (playerColumn > 0)
        {
            MoveCursor(playerRow, playerColumn);
        }

        return (playerRow, playerColumn);
    }

    private void WriteMapRow(int mapRow, char[] row, int mapWidth)
    {
        _screen.Put(mapRow + 1, 0, '|');
        _screen.Put(mapRow + 1, 1, new string(row, 0, mapWidth));
        _screen.Put(mapRow + 1, mapWidth + 1, '|');
    }
}
