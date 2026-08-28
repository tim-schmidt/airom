// AIrom - a C# port of Umoria 5.6 for Windows.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. See LICENSE.

using Airom.Core;
using Airom.Oracle;
using Airom.Terminal;

// Headless reference dumps, for diffing against the C oracle in tools/oracle.
// Checked before anything touches the console, so output stays pipe-clean.
if (args.Length > 0 && args[0] == "oracle")
{
    return OracleDump.Run(Console.Out, Console.Error, args[1..]);
}

Options options = Options.Parse(args);

ConsoleScreen screen;
try
{
    screen = ConsoleScreen.Create();
}
catch (InvalidOperationException error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

try
{
    var game = new GameState();
    var display = new Display(game, screen);
    var loop = new GameLoop(game, display);

    // The port of init_signals(): Ctrl-C becomes the suicide prompt, and the
    // console closing under a live character becomes a panic save.
    var signals = new Signals(game, display, loop);
    display.Interrupted = signals.Interrupt;
    signals.InstallConsoleHandlers();

    try
    {
        return new Session(game, display, loop).Play(options);
    }
    catch (Exception)
    {
        // The fatal half of signal_handler(). The rethrow is this port's core
        // dump: the trace prints once the terminal is put back.
        try
        {
            signals.Panic();
        }
        catch (Exception)
        {
            // The bug wins; the trace below is all that can be offered.
        }

        throw;
    }
}
finally
{
    ConsoleScreen.Restore();
}
