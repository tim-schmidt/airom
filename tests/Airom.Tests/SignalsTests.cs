using Airom.Core;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the port of signals.c: the Ctrl-C suicide prompt and the paths
/// around it. The fatal panic-save half needs a dying process to see, so only
/// the interactive branch is checked here.
/// </summary>
public class SignalsTests
{
    private const char CtrlC = '\u0003';

    private static (GameState Game, Display Display, MemoryScreen Screen, GameLoop Loop)
        Fresh(string keys)
    {
        var game = new GameState();
        game.InitSeeds(12345);
        game.MagicInit();

        Player player = new CharacterCreation(game)
            .Create(race: 0, characterClass: 0, male: true, name: "Oracle");
        game.Player = player;

        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();
        game.CharacterRow = 10;
        game.CharacterColumn = 10;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(keys);

        var display = new Display(game, screen);
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        var loop = new GameLoop(game, display);
        display.Interrupted = new Signals(game, display, loop).Interrupt;
        return (game, display, screen, loop);
    }

    /// <summary>
    /// A Ctrl-C is answered about rather than obeyed, and declining hands back
    /// the key wait as if nothing happened: the next real key is the answer.
    /// </summary>
    [Fact]
    public void Interrupt_Declined_ReadsOn()
    {
        (GameState game, Display display, _, GameLoop loop) = Fresh(CtrlC + "nx");
        game.CharacterGenerated = true;

        Assert.Equal('x', display.ReadKey());
        Assert.False(loop.Dead);
    }

    /// <summary>The prompt is the original's, word for word.</summary>
    [Fact]
    public void Interrupt_AsksTheOriginalQuestion()
    {
        (GameState game, Display display, MemoryScreen screen, _) = Fresh(CtrlC + "nx");
        game.CharacterGenerated = true;

        // The prompt is erased once it is answered, so it has to be caught
        // while the answer is being waited for.
        string seen = string.Empty;
        screen.BeforeReadKey = () => seen += screen.GetRow(0) + '\n';

        display.ReadKey();

        Assert.Contains("Really commit *Suicide*? [y/n]", seen);
    }

    /// <summary>
    /// Accepting the prompt kills the character from wherever the key wait
    /// was, by throw: died_from and death are the handler's exact settings.
    /// </summary>
    [Fact]
    public void Interrupt_Accepted_EndsTheGame()
    {
        (GameState game, Display display, _, GameLoop loop) = Fresh(CtrlC + "y");
        game.CharacterGenerated = true;

        Assert.Throws<GameInterruptedException>(() => display.ReadKey());
        Assert.True(loop.Dead);
        Assert.Equal("Interrupting", game.DiedFrom);
    }

    /// <summary>
    /// Before a character exists there is nothing to ask about: the original
    /// calls that death "Abortion" and quits without a prompt.
    /// </summary>
    [Fact]
    public void Interrupt_WithoutACharacter_IsAnAbortion()
    {
        (GameState game, Display display, _, GameLoop loop) = Fresh(CtrlC.ToString());

        Assert.Throws<GameInterruptedException>(() => display.ReadKey());
        Assert.True(loop.Dead);
        Assert.Equal("Abortion", game.DiedFrom);
    }

    /// <summary>Can't quit after death: a Ctrl-C over the tomb is swallowed.</summary>
    [Fact]
    public void Interrupt_AfterDeath_IsIgnored()
    {
        (GameState game, Display display, _, GameLoop loop) = Fresh(CtrlC + "x");
        game.CharacterGenerated = true;
        loop.Dead = true;

        Assert.Equal('x', display.ReadKey());
    }

    /// <summary>
    /// With no handler installed - the oracle, the tests - Ctrl-C is an
    /// ordinary key and comes straight back.
    /// </summary>
    [Fact]
    public void Interrupt_WithoutAHandler_IsAKey()
    {
        (_, Display display, _, _) = Fresh(CtrlC.ToString());
        display.Interrupted = null;

        Assert.Equal(CtrlC, display.ReadKey());
    }

    /// <summary>
    /// A Ctrl-C during a -more-, declined, leaves the wait standing and the
    /// game carries on once it is acknowledged.
    ///
    /// FAITHFUL QUIRK: the wait it leaves standing is invisible. The prompt's
    /// own Print flushes the pending message through a nested -more- - the
    /// original's prt() does the same - and that nested wait clears the
    /// wait_for_more flag on its way out, so the handler's repaint of the
    /// outer " -more-" never fires, there or here. The player declines
    /// suicide and is left looking at a blank message line that wants a
    /// space. The repaint code is kept because the original has it, dead as
    /// it is in this, its only path.
    /// </summary>
    [Fact]
    public void Interrupt_DuringMore_LeavesTheWaitStanding()
    {
        (GameState game, Display display, MemoryScreen screen, _) = Fresh(string.Empty);
        game.CharacterGenerated = true;

        // The second message forces a -more- for the first; the Ctrl-C lands
        // inside that wait, the first space acknowledges the nested -more-,
        // the 'n' declines, and the last space acknowledges the outer wait.
        display.MessagePrint("One thing happens here, at considerable length.");
        screen.SendKeys(CtrlC + " n ");

        display.MessagePrint("Another thing happens, also at considerable length.");

        // Every key was wanted, and the game came out the other side showing
        // the second message.
        Assert.Equal(0, screen.PendingKeys);
        Assert.StartsWith("Another thing happens", screen.GetRow(0).TrimStart());
    }
}
