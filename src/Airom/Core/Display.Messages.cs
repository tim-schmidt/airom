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

    /// <summary>
    /// Ctrl-C, which reaches the game as a key because the console is told not
    /// to raise it as an event. The original received it as SIGINT instead;
    /// see <see cref="Display.Interrupted"/>.
    /// </summary>
    public static readonly char Interrupt = Control('C');

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

    /// <summary>
    /// The furthest column a " -more-" can start from and still fit, which is
    /// also as far as a question may run before " [y/n]" goes there instead.
    /// Column 73 in the original, and seven short of the edge on any screen.
    /// </summary>
    private int MoreColumn => _screen.Columns - 7;

    private readonly string[] _oldMessages = new string[SavedMessageCount];
    private int _lastMessage;

    /// <summary>
    /// Whether a message is on screen and has not yet been acknowledged. The
    /// next message has to deal with it before overwriting.
    /// </summary>
    public bool MessageWaiting { get; private set; }

    /// <summary>
    /// What a Ctrl-C does while the game waits for a key. Mirrors the SIGINT
    /// half of signal_handler() in signals.c: the session installs
    /// <see cref="Signals.Interrupt"/> here, which may put up the suicide
    /// prompt and quits by throwing <see cref="GameInterruptedException"/>.
    /// Left null - by the oracle and the tests - Ctrl-C stays an ordinary key.
    /// </summary>
    public Action? Interrupted { get; set; }

    /// <summary>
    /// Whether <see cref="Interrupted"/> is already running, in which case
    /// further Ctrl-Cs are swallowed - the original ignores all second
    /// signals for the same reason.
    /// </summary>
    private bool _inInterrupt;

    /// <summary>
    /// Whether a " -more-" is being waited on, so an interrupt that declines
    /// to quit knows to paint it back. Mirrors wait_for_more.
    /// </summary>
    private bool _waitingForMore;

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

    /// <summary>
    /// Puts the kept messages back as a saved game left them, so a restored
    /// game can still be asked what was said before it was put away.
    /// </summary>
    public void RestoreMessages(IReadOnlyList<string> messages, int last)
    {
        ArgumentNullException.ThrowIfNull(messages);

        for (int i = 0; i < SavedMessageCount; i++)
        {
            _oldMessages[i] = i < messages.Count ? messages[i] : string.Empty;
        }

        _lastMessage = last;
    }

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

            if (text is null || newLength + oldLength + 2 >= MoreColumn)
            {
                // Keep the whole -more- visible even after a very long message.
                if (oldLength > MoreColumn)
                {
                    oldLength = MoreColumn;
                }

                PutBuffer(" -more-", MessageLine, oldLength);

                _waitingForMore = true;
                try
                {
                    char key;
                    do
                    {
                        key = ReadKey();
                    }
                    while (key != ' ' && key != Keys.Escape && key != '\n' && key != '\r');
                }
                finally
                {
                    _waitingForMore = false;
                }
            }
            else
            {
                combine = true;
            }
        }

        if (!combine)
        {
            _screen.EraseLine(MessageLine + _originRow, _originColumn);
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
    public char ReadKey() => ReadKey(whenResized: null);

    /// <summary>
    /// Reads one key, and if the terminal changes size while the game waits
    /// for it, has <paramref name="whenResized"/> draw the screen over again
    /// for the new size before waiting on.
    ///
    /// Only the command prompt passes one. A prompt for a direction or a
    /// shop's menu is drawn by whoever put it up, and nobody else knows how to
    /// put it back; those waits let the screen keep what it had, and the
    /// view is refitted the next time the loop comes round.
    /// </summary>
    public char ReadKey(Action? whenResized)
    {
        Refresh();
        CommandCount = 0;

        // The keypad spells directions in whichever keyset is in force, and
        // the option can change at any time, so it is told before every wait.
        _screen.RogueLikeKeypad = _game.RogueLikeCommands;

        // A resize while waiting: a centred layout moves to the middle of
        // the new size, the command prompt draws the game for it, and either
        // way the result is shown at once.
        _screen.Resized = () =>
        {
            Recentre();
            whenResized?.Invoke();
            Refresh();
        };

        try
        {
            while (true)
            {
                char key = _screen.ReadKey();

                if (key == Keys.Redraw)
                {
                    _screen.Refresh();
                    continue;
                }

                if (key == Keys.Interrupt && Interrupted is not null)
                {
                    // A second Ctrl-C while the first is being asked about is
                    // ignored, as the original ignores all second signals.
                    if (!_inInterrupt)
                    {
                        _inInterrupt = true;
                        try
                        {
                            Interrupted();
                        }
                        finally
                        {
                            _inInterrupt = false;
                        }

                        // In case control-c was typed during msg_print, as the
                        // original puts it: the suicide prompt took the message
                        // line, so the -more- being waited on goes back up.
                        if (_waitingForMore)
                        {
                            PutBuffer(" -more-", MessageLine, 0);
                        }

                        Refresh();
                    }

                    continue;
                }

                return key;
            }
        }
        finally
        {
            _screen.Resized = null;
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
        // question runs long - the last column a " -more-" or " [y/n]" fits in.
        int column = Math.Min(prompt.Length, MoreColumn);
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

        // Clear the field, then work within it. The cursor is put back at the
        // front afterwards, which is where the typing starts.
        Put(row, column, new string(' ', length));
        MoveCursor(row, column);

        int startColumn = column;
        int endColumn = column + length - 1;
        if (endColumn > LastColumn - _originColumn)
        {
            endColumn = LastColumn - _originColumn;
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
                // A typed character carries the cursor along with it, so it
                // leads what has been typed rather than sitting at the front
                // of the field. Mirrors mvaddch(), which writes and advances.
                Put(row, column, key);
                typed.Append(key);
                column++;
                MoveCursor(row, column);
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
        Put(0, 0, '+');
        Put(0, 1, new string('-', mapWidth));
        Put(0, mapWidth + 1, '+');

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
        Put(lastMapRow + 2, 0, '+');
        Put(lastMapRow + 2, 1, new string('-', mapWidth));
        Put(lastMapRow + 2, mapWidth + 1, '+');

        Put(23, 23, "Hit any key to continue");

        if (playerColumn > 0)
        {
            MoveCursor(playerRow, playerColumn);
        }

        return (playerRow, playerColumn);
    }

    private void WriteMapRow(int mapRow, char[] row, int mapWidth)
    {
        Put(mapRow + 1, 0, '|');
        Put(mapRow + 1, 1, new string(row, 0, mapWidth));
        Put(mapRow + 1, mapWidth + 1, '|');
    }
}
