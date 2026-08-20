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
| Random number generator | Done, verified against the original |
| Seeding and item appearances | Done, verified against the original |
| Data tables | Done |
| Item / cave / store predicates | Done |
| Constants | Item, cave and creature vocabularies done; player, dungeon and inventory pending |
| Core types | 4 of 16 structs; the rest are runtime state |
| Game logic | Not started |
| Dungeon terrain primitives | Done, verified against the original |
| Rooms — plain and overlapping | Done, verified against the original |
| Tunnels and doors | Done, verified against the original |
| Staircases and start square | Done, verified against the original |
| Object index, traps, rubble | Done, verified against the original |
| Item enchantment (magic_treasure) | Done, verified against the original |
| Level population (objects and monsters) | Done, verified against the original |
| Vault and cross rooms, summoning | Done, verified against the original |
| **Complete dungeon levels (cave_gen)** | **Done, verified against the original** |
| Win monsters (depth 50+) | Done, verified against the original |
| **The complete town, shops included** | **Done, verified against the original** |
| Character creation | Done, verified against the original |

| Terminal surface | Done — System.Console, no third-party library |
| Display: panel window and map drawing | Done, verified against the original |
| Messages, prompts, input handling | Done, verified against the original |
| Status sidebar (prt_stat_block) | Done, verified against the original |
| The turn: hunger, regeneration, every timed effect | Done, verified against the original |
| Command keys, counts and the input loop | Done, verified against the original |
| Command dispatch (do_command) | Movement, quit, message recall and the map; the rest wait on their subsystems |
| Lighting: the lamp, lit rooms, what is remembered | Done, verified against the original |
| Walking, running and searching | Done, verified against the original |
| Item naming (objdes) and what the player knows | Done, verified against the original |
| The pack: carrying, stacking, weight, equipment bonuses | Done, verified against the original |
| Combat: blows, criticals, kills and their rewards | Done, verified against the original |
| Traps, chests and doors | Done, verified against the original |
| Levels, experience and stat changes | Done, verified against the original |
| The inventory screens and wielding | Not started |
| Monsters in motion (creature.c) | Not started |
| Inventory and equipment | Not started |
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

## Generated data

The object, monster, owner and appearance tables are ~800 rows of C struct
initialisers. They are generated rather than transcribed, by
`tools/gen_tables.py`, so the field mapping is written down once and the result
is reproducible:

```
python tools/gen_tables.py            # regenerate
python tools/gen_tables.py --check    # fail if the committed files are stale
```

The generator resolves `#ifdef` branches the way a compiler would, picking the
portable build, and skips character literals when matching braces — the object
table draws bows as `'}'` and arrows as `'{'`, which naive brace counting reads
as structure.

## Layout

```
src/Airom/          the game
  Core/             engine primitives (RNG, ...)
  Data/             game tables and the types they populate
  Terminal/         the screen surface that replaces curses
tests/Airom.Tests/  test suite
tools/              code generators run against the reference sources
```

## License

GPL-3.0-or-later, inherited from Umoria. See [LICENSE](LICENSE).

Umoria is copyright © 1989–2008 James E. Wilson, Robert A. Koeneke and
David J. Grabiner. This port is a derivative work and carries the same license.

Per the original authors' request, this project is deliberately *not* named
`umoria`, so it is not confused with their version.
