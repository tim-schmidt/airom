using Airom.Core;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the message line, the prompts and the scaled map.
///
/// Message sequences and 21 scaled maps are diffed against the C oracle, which
/// runs the real io.c. These pin the behaviour behind them.
/// </summary>
public class MessageTests
{
    private static (Display Display, MemoryScreen Screen) Fresh(string? keys = null)
    {
        var game = new GameState();
        var screen = new MemoryScreen();
        if (keys is not null)
        {
            screen.SendKeys(keys);
        }

        return (new Display(game, screen), screen);
    }

    /// <summary>
    /// Two short messages run onto one line, which is why a flurry of small
    /// notices does not cost a keypress each.
    /// </summary>
    [Fact]
    public void MessagePrint_CombinesTwoShortMessages()
    {
        (Display display, MemoryScreen screen) = Fresh();

        display.MessagePrint("You feel a sudden chill.");
        display.MessagePrint("It bites you.");

        Assert.Equal(
            "You feel a sudden chill.   It bites you.",
            screen.GetRow(0).TrimEnd());
    }

    /// <summary>
    /// A pair that will not fit prompts with -more- and waits, so nothing is
    /// lost by being scrolled past.
    /// </summary>
    [Fact]
    public void MessagePrint_PromptsWhenTwoWillNotFit()
    {
        (Display display, MemoryScreen screen) = Fresh(" ");

        display.MessagePrint(new string('a', 50));
        display.MessagePrint(new string('b', 50));

        // The second message ended up alone on the line.
        Assert.Equal(new string('b', 50), screen.GetRow(0).TrimEnd());
        Assert.False(screen.KeyAvailable); // the space was consumed by -more-
    }

    /// <summary>
    /// Only a space, escape or return dismisses a -more-, so a player hammering
    /// a movement key does not skip it by accident.
    /// </summary>
    [Fact]
    public void MessagePrint_MorePromptIgnoresOtherKeys()
    {
        (Display display, MemoryScreen screen) = Fresh("xyz ");

        display.MessagePrint(new string('a', 50));
        display.MessagePrint(new string('b', 50));

        Assert.False(screen.KeyAvailable); // x, y and z were all rejected
    }

    /// <summary>A null message clears the line without printing anything.</summary>
    [Fact]
    public void MessagePrint_NullFlushesTheLine()
    {
        (Display display, MemoryScreen screen) = Fresh(" ");

        display.MessagePrint("Something happened.");
        Assert.True(display.MessageWaiting);

        display.MessagePrint(null);

        Assert.False(display.MessageWaiting);
        Assert.Equal(string.Empty, screen.GetRow(0).Trim());
    }

    /// <summary>
    /// The history is a ring of twenty-two, so the oldest is overwritten rather
    /// than the newest being dropped.
    /// </summary>
    [Fact]
    public void MessagePrint_KeepsAHistoryRing()
    {
        (Display display, _) = Fresh(new string(' ', 200));

        for (int i = 0; i < Display.SavedMessageCount + 5; i++)
        {
            // Long enough that each one displaces the last rather than combining.
            display.MessagePrint($"message {i} " + new string('x', 60));
        }

        Assert.Equal(Display.SavedMessageCount, display.RecentMessages.Count);
        Assert.Contains(
            display.RecentMessages,
            m => m is not null && m.StartsWith("message 26", StringComparison.Ordinal));
    }

    /// <summary>
    /// A repeating command keeps its count across a message, which is what lets
    /// a long rest report interruptions without cancelling itself.
    /// </summary>
    [Fact]
    public void CountMessagePrint_PreservesTheCommandCount()
    {
        (Display display, _) = Fresh();

        display.CommandCount = 17;
        display.CountMessagePrint("You feel rested.");

        Assert.Equal(17, display.CommandCount);
    }

    [Fact]
    public void MessagePrint_ClearsTheCommandCount()
    {
        (Display display, _) = Fresh();

        display.CommandCount = 17;
        display.MessagePrint("Something interrupts you.");

        Assert.Equal(0, display.CommandCount);
    }

    // ------------------------------------------------------------- prompts

    /// <summary>
    /// Spaces are ignored rather than taken as a no, so mashing past a message
    /// cannot accidentally decline a question.
    /// </summary>
    [Fact]
    public void GetCheck_IgnoresSpacesAndTreatsAnythingButYesAsNo()
    {
        (Display yes, _) = Fresh("   y");
        Assert.True(yes.GetCheck("Really?"));

        (Display no, _) = Fresh("n");
        Assert.False(no.GetCheck("Really?"));

        (Display other, _) = Fresh("q");
        Assert.False(other.GetCheck("Really?"));
    }

    [Fact]
    public void GetCommand_ReportsEscapeAsRefusal()
    {
        (Display taken, _) = Fresh("k");
        Assert.True(taken.GetCommand("Direction?", out char command));
        Assert.Equal('k', command);

        (Display escaped, _) = Fresh(Keys.Escape.ToString());
        Assert.False(escaped.GetCommand("Direction?", out _));
    }

    [Fact]
    public void GetString_ReadsUntilReturn()
    {
        (Display display, _) = Fresh("Gandalf\r");

        Assert.True(display.GetString(2, 15, 23, out string text));
        Assert.Equal("Gandalf", text);
    }

    [Fact]
    public void GetString_BackspaceRubsOut()
    {
        (Display display, _) = Fresh("Gandalx" + Keys.Backspace + "f\r");

        Assert.True(display.GetString(2, 15, 23, out string text));
        Assert.Equal("Gandalf", text);
    }

    [Fact]
    public void GetString_EscapeAbandonsTheWholeThing()
    {
        (Display display, _) = Fresh("Gandalf" + Keys.Escape);

        Assert.False(display.GetString(2, 15, 23, out string text));
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void GetString_TrimsTrailingSpaces()
    {
        (Display display, _) = Fresh("Gandalf   \r");

        Assert.True(display.GetString(2, 15, 23, out string text));
        Assert.Equal("Gandalf", text);
    }

    /// <summary>
    /// Typing past the end of the field rings the bell rather than overflowing
    /// into whatever is drawn beside it.
    /// </summary>
    [Fact]
    public void GetString_RefusesToRunPastTheField()
    {
        (Display display, MemoryScreen screen) = Fresh(new string('x', 10) + "\r");

        Assert.True(display.GetString(2, 15, 5, out string text));
        Assert.Equal(5, text.Length);
        Assert.True(screen.BellCount > 0, "no bell when the field overflowed");
    }

    // ----------------------------------------------------------- input

    /// <summary>
    /// A redraw request is handled by the reader itself rather than reaching the
    /// game, so no command has to know about it.
    /// </summary>
    [Fact]
    public void ReadKey_SwallowsTheRedrawKey()
    {
        (Display display, _) = Fresh(Keys.Redraw.ToString() + "j");

        Assert.Equal('j', display.ReadKey());
    }

    [Fact]
    public void Bell_CanBeTurnedOff()
    {
        var game = new GameState();
        var screen = new MemoryScreen();
        var display = new Display(game, screen);

        display.Bell();
        Assert.Equal(1, screen.BellCount);

        game.SoundEnabled = false;
        display.Bell();
        Assert.Equal(1, screen.BellCount);
    }
}
