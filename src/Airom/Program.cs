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
using Airom.Data;
using Airom.Oracle;
using Airom.Terminal;

// Headless reference dumps, for diffing against the C oracle in tools/oracle.
// Checked before anything touches the console, so output stays pipe-clean.
if (args.Length > 0 && args[0] == "oracle")
{
    return OracleDump.Run(Console.Out, Console.Error, args[1..]);
}

// The game is not playable yet. Until it is, the entry point exercises the
// pieces that exist: it paints a frame through the real terminal layer and
// reports whether the generator still matches the sequence Umoria produced.

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
    screen.Put(1, 2, "AIrom");
    screen.Put(2, 2, "Umoria 5.6, ported to C#");

    screen.Put(4, 2, $"Objects loaded   : {GameTables.ObjectList.Length}");
    screen.Put(5, 2, $"Creatures loaded : {GameTables.CreatureList.Length}");
    screen.Put(6, 2, $"Store owners     : {GameTables.Owners.Length}");

    // Umoria's rnd.c carried this self-check under #ifdef TEST_RNG. Every
    // dungeon the game will ever generate comes off this sequence, so it is
    // worth reporting on sight.
    var rng = new Rng(0);
    int value = 0;
    for (int i = 0; i < 10_000; i++)
    {
        value = rng.Next();
    }

    const int Expected = 1043618065;
    screen.Put(
        8,
        2,
        value == Expected
            ? $"RNG conformance  : ok (z[10001] = {value})"
            : $"RNG conformance  : FAILED (got {value}, expected {Expected})");

    screen.Put(10, 2, "Nothing to play yet. Press any key to exit.");
    screen.MoveCursor(10, 45);
    screen.Refresh();

    if (!Console.IsInputRedirected)
    {
        screen.ReadKey();
    }

    return value == Expected ? 0 : 1;
}
finally
{
    ConsoleScreen.Restore();
}
