# The oracle

Umoria has no test suite. It never had one. So "is the port correct?" has no
definition except *does it do what the C did* — which means the C itself has to
be runnable and comparable.

That is what this directory is. It builds the original Umoria 5.6 sources into a
headless program that prints its internal state as plain text, so the same run
against AIrom can be diffed line for line.

## Why it earns its keep

The game is fully deterministic. Dungeon layout, room carving, corridor
tunnelling, monster placement, item drops and the town map all derive from a
single Park–Miller generator. So a matching dump is not a shallow check — it
means the entire chain that produced it agrees, across thousands of lines, from
one comparison.

Nothing else available is close to as broad for the effort.

## Why it is affordable

The oracle does not need a *playable* build of 1989 C on Windows. It needs
generate-and-print. So it excludes the five files that made Umoria hard to build
here in the first place:

| Excluded | Why |
|---|---|
| `io.c` | curses, termios, `ioctl` |
| `death.c` | `setuid`, `flock`, the shared scoreboard |
| `signals.c` | Unix signal handling |
| `files.c` | score file and help file I/O |
| `help.c` | interactive help |
| `main.c` | replaced by `oracle_main.c` |

`oracle_stubs.c` supplies those symbols. Output stubs are silent; input stubs
abort loudly, because nothing in dungeon generation should ever ask for a
keypress — if one does, the run is not measuring what it claims to.

Worth noting: `DEBIAN_LINUX`, which `config.h` defines by default, appears *only*
in the excluded files. The game logic compiles identically either way, which is
what makes the C# port's "treat every platform symbol as undefined" choice
consistent with this build.

## Building

Needs a C compiler, which the project does not otherwise require:

```
winget install MSYS2.MSYS2
C:\msys64\usr\bin\pacman -S --noconfirm mingw-w64-ucrt-x86_64-gcc
```

Then:

```
CC=/c/msys64/ucrt64/bin/gcc.exe tools/oracle/build.sh
```

It compiles at `-std=gnu89`, with warnings off. That is deliberate: this is
K&R-era C where implicit declarations and old-style definitions are the norm,
not mistakes. The goal is a faithful build of 1989 code, not a tidy one.

## Comparing

```
tools/oracle/compare.sh rng   <seed> <count>
tools/oracle/compare.sh seeds <seed>
tools/oracle/compare.sh cave  <seed> <level>
```

Each side prints the same format and the script diffs them, reporting the first
divergence.

## Modes

| Mode | Compares | Status |
|---|---|---|
| `rng` | Raw generator draws from a seed | **Verified matching** |
| `streamers` | Terrain primitives: fill, mineral veins, vein gold, boundary | **Verified matching** |
| `rooms` | One room builder over the whole room grid, terrain and lit-room marks | **Verified matching** |
| `tunnels` | Rooms joined by corridors, plus every door left behind | **Verified matching** |
| `stairs` | The whole terrain half of cave_gen, through to the player's start square | **Verified matching** |
| `picks` | The depth-sorted object index and the draws that read it | **Verified matching** |
| `enchanted` | magic_treasure: generated items with every bonus, flag, charge and price | **Verified matching** |
| `populate` | A complete level: terrain, every object on it, and every monster | **Verified matching** |
| `seeds` | `init_seeds` chain, `magic_init`, the shuffled appearance tables | **Verified matching** |
| `cave` | **A complete dungeon level**: every room type, terrain, lighting, monsters, objects | **Verified matching** |
| `town` | The town: shops, doors, stairs, lighting, townsfolk (less shop restocking) | **Verified matching** |

`seeds` is the one that will confirm the `reset_seed` quirk against the original
rather than by inference: `magic_init` shuffles appearances inside a
`set_seed`/`reset_seed` bracket, and the restore deliberately does not land
where it started.

## Reaching inside generate.c and main.c

Everything in `generate.c` is `static` except `generate_cave()`, so linking
against it allows comparing whole finished levels and nothing smaller. That is
no use while the port is being built a layer at a time.

`oracle_probe.c` solves it by `#include`-ing the source rather than linking it,
which reaches the file's statics while leaving the reference tree untouched —
no patched copy to reconcile later. `generate.c` is excluded from the build's
file list precisely because it arrives through the probe instead.

`oracle_probe_main.c` does the same for `main.c`, whose static `init_t_level()`
builds the depth-sorted object index. That file also defines the game's own
`main()`, so the probe renames it before including. Reimplementing the sort in
the harness instead would only have proved that two copies written for this
project agreed — not that either matched Umoria.

## Format

Line-oriented ASCII, one fact per line, bare LF endings, so a diff points at the
first real divergence instead of a wall of noise.

```
# airom-oracle 1
mode rng
seed 7
count 3
state-after-set-seed 8
value 1 134456
value 2 112318345
value 3 96298702
final-state 96298702
```

The version on the first line is `ORACLE_FORMAT` in `oracle_main.c` and
`OracleDump.FormatVersion` in the C#. Bump both together.

## Current verification

The C oracle builds with gcc 16.1.0 (MSYS2 UCRT64) and `rng` matches AIrom
exactly: **20,000 draws across seven seeds — 140,000 values** — including the
boundaries 0, 1, `M-1` and `UINT_MAX`, and the folded start state and final
state in each.

`seeds` matches too — **64 seeds, 217 lines each**, covering the seeding chain,
all six appearance shuffles and all 45 generated scroll titles. It confirms the
`reset_seed` quirk by measurement rather than inference:

```
state-after-init-seeds  1737948946
state-after-magic-init  1737948947
```

Exactly one higher, because `magic_init` brackets its shuffle in
`set_seed`/`reset_seed` and the restore folds an already-in-range value through
`set_rnd_seed` again. AIrom reproduces that on purpose; this is the measurement
that says so.
