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
| `seeds` | `init_seeds` chain, `magic_init`, the shuffled appearance tables | C side works; C# side waiting on the port |
| `cave` | A generated level: terrain, lighting flags, monsters, objects | C side written; C# side waiting on the port |

`seeds` is the one that will confirm the `reset_seed` quirk against the original
rather than by inference: `magic_init` shuffles appearances inside a
`set_seed`/`reset_seed` bracket, and the restore deliberately does not land
where it started.

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

The `seeds` mode already runs on the C side and confirms the `reset_seed` quirk
empirically rather than by inference:

```
state-after-init-seeds  1737948946
state-after-magic-init  1737948947
```

Exactly one higher, because `magic_init` brackets its shuffle in
`set_seed`/`reset_seed` and the restore folds an already-in-range value through
`set_rnd_seed` again. AIrom reproduces that on purpose; this is the measurement
that says so.
