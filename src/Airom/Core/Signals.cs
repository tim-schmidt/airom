// Ported from Umoria 5.6 source/signals.c - what an interrupt and a dying
// process do to a running game.
//
// The original installed signal handlers for everything. Here Ctrl-C arrives
// as a key, because the terminal is told to pass it through, and reaches
// Interrupt() from inside the game's own key wait - which is where a DOS
// signal effectively arrived too, delivery there being tied to I/O. A fatal
// error reaches Panic() from the handler of last resort around the whole game.
// Only the terminal going away is still a signal: a hangup or a termination
// reaches PanicQuietly() through PosixSignalRegistration, which .NET delivers
// as a real signal on macOS and Linux and as the matching console event on
// Windows - the window closing, a logoff, a shutdown, Ctrl-Break.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Runtime.InteropServices;

namespace Airom.Core;

/// <summary>
/// Thrown when the player answers yes to the suicide prompt, or interrupts a
/// game not worth asking about. It unwinds the whole stack - the original
/// handler could call exit_game() from wherever the signal landed - and is
/// caught in <see cref="Session.Play"/>, which buries the character.
/// </summary>
public sealed class GameInterruptedException : Exception
{
    public GameInterruptedException()
    {
    }

    public GameInterruptedException(string message)
        : base(message)
    {
    }

    public GameInterruptedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>The port of signal_handler(), split by what a modern console can deliver.</summary>
public sealed class Signals
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Signals(GameState game, Display display, GameLoop loop)
    {
        _game = game;
        _display = display;
        _loop = loop;
    }

    /// <summary>
    /// A Ctrl-C during play. Mirrors the SIGINT branch of signal_handler():
    /// the player is allowed to think twice, and declining leaves the game
    /// exactly where it was.
    /// </summary>
    public void Interrupt()
    {
        if (_loop.Dead)
        {
            // Can't quit after death.
            return;
        }

        if (_game.CharacterGenerated && !_game.CharacterSaved)
        {
            if (!_display.GetCheck("Really commit *Suicide*?"))
            {
                if (_game.Turn > 0)
                {
                    _loop.Disturb(true, false);
                }

                _display.EraseLine(Display.MessageLine, 0);
                return;
            }

            _game.DiedFrom = "Interrupting";
        }
        else
        {
            _game.DiedFrom = "Abortion";
        }

        _display.Print("Interrupt!", 0, 0);
        _loop.Dead = true;
        throw new GameInterruptedException();
    }

    /// <summary>
    /// A software error the game did not survive. Mirrors the fatal half of
    /// signal_handler(): a living character gets a panic save, marked so the
    /// restored game is never scored, and anyone else gets the monster memory
    /// written out quietly. The caller rethrows afterwards - the stack trace
    /// is this port's core dump.
    /// </summary>
    public void Panic()
    {
        // No interrupting the guardian angel: the save may prompt, and a
        // Ctrl-C inside it has nothing sensible to mean any more.
        _display.Interrupted = null;

        _display.Print(
            "OH NO!!!!!!  A gruesome software bug LEAPS out at you. There is NO defense!",
            23, 0);

        if (!_loop.Dead && !_game.CharacterSaved && _game.CharacterGenerated)
        {
            _game.PanicSaved = true;
            _display.Print("Your guardian angel is trying to save you.", 0, 0);
            _display.Refresh();
            _game.DiedFrom = "(panic save)";

            if (!_loop.SaveFile.SaveWithRetry())
            {
                _game.DiedFrom = "software bug";
                _loop.Dead = true;
                _game.Turn = -1;
            }
        }
        else
        {
            _loop.Dead = true;

            // Quietly save the memory anyway.
            _loop.SaveFile.Save(_loop.SaveFile.CurrentPath);
        }
    }

    /// <summary>
    /// The console is going away - its window closed, Ctrl-Break, a logoff or
    /// a shutdown - so there is nobody to ask anything. The nearest the
    /// original came was a hangup, which it answered with a panic save; this
    /// does the same and lets the process end.
    /// </summary>
    private void PanicQuietly()
    {
        if (!_game.CharacterGenerated || _game.CharacterSaved || _loop.Dead)
        {
            return;
        }

        _game.PanicSaved = true;
        _game.DiedFrom = "(panic save)";

        // Save() would ask a wizard before overwriting a file it did not read,
        // and a question cannot be put to a console that is closing. Claiming
        // the file was read skips the question; the process is over either way.
        _loop.SaveFile.FromSaveFile = true;
        _loop.SaveFile.Save(_loop.SaveFile.CurrentPath);
    }

    /// <summary>
    /// The signals that mean the terminal is gone or going. On Windows .NET
    /// raises SIGHUP for the console window closing, SIGTERM for a logoff or
    /// a shutdown, and SIGQUIT for Ctrl-Break - the four events this game has
    /// always answered with a panic save.
    /// </summary>
    private static readonly PosixSignal[] Hangups =
        [PosixSignal.SIGHUP, PosixSignal.SIGTERM, PosixSignal.SIGQUIT];

    /// <summary>
    /// Kept alive in a field: a registration is undone when it is collected.
    /// </summary>
    private readonly List<PosixSignalRegistration> _registrations = [];

    /// <summary>
    /// Registers for the signals a hangup arrives as. The handler runs on a
    /// thread of the runtime's choosing while the game is in the middle of
    /// whatever it was doing; a torn save is possible, and is still better
    /// than the certain loss of not writing one.
    /// </summary>
    public void InstallHangupHandlers()
    {
        foreach (PosixSignal signal in Hangups)
        {
            _registrations.Add(PosixSignalRegistration.Create(signal, OnHangup));
        }
    }

    private void OnHangup(PosixSignalContext context)
    {
        try
        {
            PanicQuietly();
        }
        catch (Exception)
        {
            // The process is ending; there is nowhere left to report to.
        }

        // A closing console ends the process when this returns anyway; the
        // others would play on with a game already marked saved, so they end
        // the same way.
        Environment.Exit(1);
    }
}
