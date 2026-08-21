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

Playable. Every file of the original has been ported and diffed against it;
what remains is play-testing rather than porting.

```
airom              play, picking up a saved game if there is one
airom -n           start a new character
airom -S           the score table
airom -w           wizard mode, which forfeits the score
```

A game saved by a real Umoria 5.6 can be opened here, and one saved here can
be taken back to it: the savefile is the original's, byte for byte.

| Area | State |
|---|---|
| Random number generator | Done, verified against the original |
| Seeding and item appearances | Done, verified against the original |
| Data tables | Done |
| Item / cave / store predicates | Done |
| Constants | Done |
| Core types | Done |
| Game logic | Done, verified against the original |
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
| Command dispatch (do_command) | Everything but the character sheet, spiking a door, the help files and saving |
| Lighting: the lamp, lit rooms, what is remembered | Done, verified against the original |
| Walking, running and searching | Done, verified against the original |
| Item naming (objdes) and what the player knows | Done, verified against the original |
| The pack: carrying, stacking, weight, equipment bonuses | Done, verified against the original |
| Combat: blows, criticals, kills and their rewards | Done, verified against the original |
| Traps, chests and doors | Done, verified against the original |
| Levels, experience and stat changes | Done, verified against the original |
| The inventory screens, wearing and wielding | Done, verified against the original |
| Monsters in motion: moving, breeding, waking, attacking | Done, verified against the original |
| Monster spells and breaths | Done, verified against the original |
| Spell engine: bolts, balls, breaths, and the spells aimed at monsters | Done, verified against the original |
| Potions and food, and what they do | Done, verified against the original |
| The cures, the losses and the small comforts from spells.c | Done, verified against the original |
| Scrolls, wands and staffs, and everything they do | Done, verified against the original |
| The rest of spells.c: detection, enchantment, earthquakes, destruction | Done, verified against the original |
| The player's own spellcasting (magic.c, prayer.c) | Done, verified against the original |
| Learning spells, mana and the spell list (misc3.c) | Done, verified against the original |
| Digging, disarming, bashing and throwing (moria4.c) | Done, verified against the original |
| Looking around, with peripheral vision | Done, verified against the original |
| The shops: stock, prices and haggling (store1.c, store2.c) | Done, verified against the original |
| The monster memory, written out as prose (recall.c) | Done, verified against the original |
| What a symbol on the map means (help.c) | Done, verified against the original |
| The debugging commands (wizard.c) | Done, verified against the original |
| Dying, the tomb and the character sheet (death.c, files.c) | Done, verified against the original |
| The score table itself | Done, rewritten for one player on one machine |
| Save files (save.c) | Done, verified against the original |
| Rolling a character, with prompts (create.c) | Done, verified against the original |
| Starting the game: arguments, the play loop (main.c) | Done |

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

Where the 1989 code has quirks, the port keeps them and documents why —
`(!noscore & 0x04)`, which is always false and has never once run; a help file
that ends on a blank page; "the Balrog" keeping its article on the score
board. Such places are marked `FAITHFUL QUIRK` in the source.

One is not kept. `reset_seed()` restores a saved generator state to the *next*
value rather than the saved one, and since the generator is multiplicative that
lands on an unrelated part of the cycle: a "reset" that resets nothing. It was
reproduced for as long as the port needed to be diffed against the original,
which is what made the differential testing possible at all, and was corrected
once that testing was done. The oracle harness still asks for the old behaviour,
because the C it compares against will always have it.

The one thing this changes is which dungeon a given seed produces. AIrom's seed
1 and Umoria's seed 1 are different games from the first level down — every
other observable, including the savefile, is unchanged.

One place is deliberately not a port. Umoria's score table is a single file
shared by every player on a Unix machine: the game runs setuid, locks the file
while it writes, and stamps each entry with a user id. None of that means
anything for one person on one Windows machine, so the table is kept as a plain
file under the player's own application data, with no lock and a user id of
nought throughout - which is the case the original already handles, falling back
to the character's birth date to tell one character from another. The *record*
inside that file is still the original's, byte for byte, and is compared as
such.

Save files are the original's, byte for byte. A game saved by a real Umoria
5.6 can be picked up here and a game saved here can be taken back, which is
what the version bytes at the front of every file are for: the format was
frozen at 5.2.2, and everything from 5.0.14 on is read.

The C sources are kept outside this repository and used strictly as reference.

## The limits

Umoria's `MAX_*` constants are 1989 memory budgets rather than design. They
were left exactly as they were for as long as the port was being diffed against
the original, since every one of them changes what a seed produces and a
changed one turns the comparison into noise. That is over, so here is what each
group actually costs to move.

| Group | Examples | What holds it |
|---|---|---|
| Balance | `MAX_SIGHT` 20, `MAX_MON_MULT` 75, `MAX_PLAYER_LEVEL` 40 | Nothing. These are the game's dials, and turning one is a deliberate change to how it plays. `MAX_PLAYER_LEVEL` also sizes the saved hit-point table, so it needs a save-format bump. |
| Counter ceilings | `MAX_SHORT` 32767, `MAX_UCHAR` 255 | The savefile, which stores those counters in two bytes and one. They are not tunable on their own. |
| List sizes | `MAX_TALLOC` 175, `MAX_MALLOC` 125 | The savefile again: a level's object and monster indices are written one byte each, so 255 is the ceiling until the format changes. Raising them within that is safe — a full list compacts rather than failing. |
| Content | `MAX_OBJECTS` 420, `MAX_CREATURES` 279 | Append freely; never insert or reorder. Rows are addressed by position by the `OBJ_*` constants, by the win-monster arithmetic, and by every savefile ever written. |
| Dungeon size | `MAX_HEIGHT` 66, `MAX_WIDTH` 198 | Real work. The panel arithmetic wants a whole number of half-screens, not the byte-wide coordinate fields, which reach 255. Also a save-format bump: the cave is swept as `MAX_HEIGHT * MAX_WIDTH` and range-checked on the way back in. |

Nothing in the port depends on any of these being what they are, beyond the
savefile compatibility noted above — the game reads its own limits everywhere,
and a level that fills one compacts to make room rather than giving up.

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
help/               the original's help text, shipped beside the program
tests/Airom.Tests/  test suite
tools/              code generators run against the reference sources
```

## License

GPL-3.0-or-later, inherited from Umoria. See [LICENSE](LICENSE).

Umoria is copyright © 1989–2008 James E. Wilson, Robert A. Koeneke and
David J. Grabiner. This port is a derivative work and carries the same license.

Per the original authors' request, this project is deliberately *not* named
`umoria`, so it is not confused with their version.
