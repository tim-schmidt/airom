# AIrom

A C# port of [Umoria](https://en.wikipedia.org/wiki/Moria_(video_game)) 5.6 —
the 1980s dungeon-crawling roguelike — targeting Windows as a native terminal
application.

The name is *Moria* backwards.

## Why

The original is K&R C from 1989, written for UNIX and later carried to a dozen
dead platforms. The practical way to play it on Windows today is a DOS build
under DOSBox. This port removes that step: one self-contained `.exe`, no
emulator, no runtime install.

## Status

Early. The foundations are in place; the game is not yet playable.

| Area | State |
|---|---|
| Random number generator | Done, conformance-tested |
| Data tables | Not started |
| Game logic | Not started |
| Terminal I/O | Not started |
| Save files | Not started |

## Building

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download).

```
dotnet test                              # run the test suite
dotnet run --project src/Airom           # run from source
dotnet publish src/Airom -c Release      # produce a standalone exe
```

The published binary lands in
`src/Airom/bin/Release/net9.0/win-x64/publish/airom.exe` and needs nothing
installed to run.

## Fidelity

The goal is the *same game*, not a game like it. Umoria derives every dungeon,
monster roll and town layout from a single Park–Miller generator, so the port is
verified against the original's own published check value — seeded at 1, the
10,001st draw must be `1043618065`. That test runs on every build.

Where the 1989 code has quirks, the port keeps them and documents why. The
clearest example is `reset_seed()`, which restores a saved generator state to
the *next* seed value rather than the saved one — and since the generator is
multiplicative, that lands on an unrelated part of the cycle. Harmless in play,
but "fixing" it would change which dungeons a given seed produces. Such places
are marked `FAITHFUL QUIRK` in the source.

The C sources are kept outside this repository and used strictly as reference.

## Layout

```
src/Airom/          the game
  Core/             engine primitives (RNG, ...)
tests/Airom.Tests/  test suite
```

## License

GPL-3.0-or-later, inherited from Umoria. See [LICENSE](LICENSE).

Umoria is copyright © 1989–2008 James E. Wilson, Robert A. Koeneke and
David J. Grabiner. This port is a derivative work and carries the same license.

Per the original authors' request, this project is deliberately *not* named
`umoria`, so it is not confused with their version.
