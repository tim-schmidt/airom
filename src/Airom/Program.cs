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

Console.WriteLine("AIrom - Umoria 5.6, ported to C#");
Console.WriteLine();

// Umoria's rnd.c carried a self-check under #ifdef TEST_RNG. Keeping it visible
// here is a cheap smoke test that the generator driving every dungeon still
// matches the original.
var rng = new Rng(0);
int value = 0;
for (int i = 0; i < 10_000; i++)
{
    value = rng.Next();
}

Console.WriteLine(value == 1043618065
    ? $"RNG conformance: ok (z[10001] = {value})"
    : $"RNG conformance: FAILED (z[10001] = {value}, expected 1043618065)");
