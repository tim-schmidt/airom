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
| `signals.c` | Unix signal handling |
| `main.c` | replaced by `oracle_main.c` |
| `generate.c`, `dungeon.c`, `moria3.c`, `death.c` | compiled, but through a probe that reaches their static functions |

`help.c`, `files.c` and `death.c` were on that list once, for the terminal and
the Unix kernel they wanted. They are all compiled now: what they actually
needed was a screen and a few headers, and the fake curses has supplied the
screen since io.c was brought in.

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
| `town` | **The complete town**: shops, doors, stairs, lighting, townsfolk, shop stock | **Verified matching** |
| `shops` | Shop owners, stock and asking prices across repeated restocks | **Verified matching** |
| `character` | A rolled character: stats, history, build, hit point curve, money | **Verified matching** |
| `screen` | The drawn map, composed by the real io.c through the panel arithmetic | **Verified matching** |
| `messages` | The message line: combining, -more- prompting, the history ring | **Verified matching** |
| `map` | The whole level shrunk to one screen, as the M command shows it | **Verified matching** |
| `statblock` | The status sidebar: identity, stats, numbers and every condition indicator | **Verified matching** |
| `commands` | Every key through the original-to-rogue translation and the count table | **Verified matching** |
| `regen` | Hit point and mana regeneration, fraction carrying and the clamps | **Verified matching** |
| `upkeep` | **A turn in the dungeon**: the real dungeon() loop, counter by counter | **Verified matching** |
| `hallucinate` | The map drawn by a hallucinating character, rolls included | **Verified matching** |
| `light` | **A walk, lit**: what the player sees as they move, step by step | **Verified matching** |
| `walk` | **Steps**: move_char over a scripted path, walls and searching included | **Verified matching** |
| `run` | **A run**: the whole find algorithm, path dumped square by square | **Verified matching** |
| `search` | Finding traps, secret doors and the trap on a chest | **Verified matching** |
| `names` | **Every item in the table named eight ways**, known and unknown | **Verified matching** |
| `pickup` | Walking over things and carrying them: the purse, the pack, the weight | **Verified matching** |
| `fight` | **Hitting things**: every blow, every kill, the drops and the memory | **Verified matching** |
| `traps` | **Every trap in the table**, sprung on a fresh character each | **Verified matching** |
| `monsters` | **The monsters taking their turns**: moving, breeding, stealing, attacking, casting | **Verified matching** |
| `potion` | **Every potion and mushroom in the table**, drunk or eaten and compared | **Verified matching** |
| `scroll` | **Every scroll in the table**, read on a generated level and compared | **Verified matching** |
| `wand` | **Every wand in the table**, aimed east down a generated level | **Verified matching** |
| `staff` | **Every staff in the table**, used on a generated level | **Verified matching** |
| `spell` | **Every mage spell**, cast twice on a generated level - with mana and without | **Verified matching** |
| `prayer` | **Every prayer**, recited the same way | **Verified matching** |
| `inven` | **Twenty scripted runs of the inventory mode**, screen and pack compared | **Verified matching** |
| `getitem` | **Ten runs of the prompt that asks which item** | **Verified matching** |
| `moria4` | **Ten scripted arrangements**: digging, disarming, bashing, throwing | **Verified matching** |
| `look` | **Thirty-six looks**, every direction with and without mineral veins, and again recalling what it finds | **Verified matching** |
| `store` | **Seventeen scripted visits** through each of the six shops | **Verified matching** |
| `recall` | **Every creature described**, at four depths of knowledge and sixteen levels | **Verified matching** |
| `symbol` | **Every printable symbol asked about**, with and without a memory to offer | **Verified matching** |
| `wizard` | **The debugging commands**: lighting, editing a character, building an item | **Verified matching** |

`seeds` is the one that will confirm the `reset_seed` quirk against the original
rather than by inference: `magic_init` shuffles appearances inside a
`set_seed`/`reset_seed` bracket, and the restore deliberately does not land
where it started.

## Driving the real dungeon()

`upkeep` runs Umoria's own main loop rather than a reimplementation of it. A
headless harness cannot type, so the character is paralysed for the length of
the run - no command is asked for while paralysis lasts - and a quit is left in
the key script for the turn it wears off. Every counter therefore ages through
the original code.

The monsters are cleared off the level first. They move now, so the reason has
changed: a monster reaching the player would fight, and the fight would drown
out the turn being compared. `monsters` is the mode that leaves them on.

Spellcasters cast now. One variation of that mode rings the player with
everything at the depth that has spells, awake and in range, so the breaths,
summonings and drains are compared rather than waited for - a level left to
itself rarely brings a caster into line of sight.

`walk`, `run` and `search` strip the level of monsters and loose objects
first; `pickup` keeps the objects and strips only the monsters and the traps,
since `hit_trap()` is not ported. Monsters would move on the C side only, since `creature.c` is not
ported; objects would be picked up on the C side only, since `carry()` needs
the inventory, and picking one up prints a message and changes the pack. The
`search` mode then puts back exactly what it wants to find - one of each trap,
a secret door and a trapped chest - around the player.

`light` covers what a stationary character never reaches. `move_char()` belongs
to `moria2.c` and is not ported, so the walk is done in the harness itself: it
picks a square, moves the player record, and tells the lighting about it, which
is the sequence `move_char()` uses. Each variation takes a different path
through `move_light()` - lamp, blind, no lamp at all, and running.

## Driving the real quaff() and eat()

Both take an item from the pack through the inventory screen, which is not
ported. The C harness puts the item in the pack and feeds the letter, so the
whole of `quaff()` runs - prompting, effects, the experience for working out
what it was, the food and the item being used up. This side calls the effects
directly and then does the same bookkeeping around them.

Two things had to be levelled out to make that comparison mean anything.
`magic_init()` shuffles the appearance tables where they stand, so it runs once
rather than once per item - calling it twice would shuffle an already-shuffled
table. And what the player knows is forgotten between items, so every potion is
drunk by someone who has never seen one.

## Driving the real read_scroll(), aim() and use()

These reach much further than a potion does - a scroll can wall the player in, a
wand can dissolve a corridor, a staff can shake the level apart - so they are
compared on a freshly generated level rather than in an empty room, and the
level is dumped as a set of counts beside the player: monsters left standing,
objects left lying, squares lit, squares marked, squares walled.

They also ask questions part-way through. The C harness answers them with a
scripted key: `a` picks the first pack slot, `6` points east, `k` is the letter
fed to a scroll of genocide. Which of those a given item needs is known from its
flags, so the script is built to match, and the padding is escapes rather than
spaces - a space answers none of these prompts, so a prompt given one more key
than the script provides would spin on the padding instead of giving up.

Two subtleties came out of that. Writing a prompt to the message line flushes
whatever message was waiting there, so a scroll that announces itself before it
asks loses the announcement to a `-more-` that eats a scripted key; the script
allows one key for it, and this side's stand-in for the prompt flushes the same
way. And the character is given a weapon, a suit of armour, a cap and a lit
torch: without a light nothing can be read at all, and without something worn
the enchanting and cursing scrolls have nothing to work on.

## Driving the real cast() and pray()

Every spell a class has is cast twice by the same character on the same
generated level: once with mana to spare and once with almost none. The second
pass is not padding - being short changes how likely the spell is to fail, adds
a question before it goes off at all, and costs consciousness instead of mana
when it does.

The prompts are answered by a scripted key, as the scrolls are: "a" picks the
book, the spell's own letter picks the spell, "y" presses on with a spell that
cannot be afforded, "6" points east, and "k" is the letter fed to genocide.
Which of those a given spell needs is known from its number, so both sides build
the same script - except for the book, which this side answers without a key,
so its script starts one letter later.

Two things had to be set up in the right order. The spells a character knows are
kept outside the player struct in the original, so they survive a reset that
clears everything else and have to be cleared by hand between casts; and they
are set after the stats rather than before, since setting a casting stat is what
makes the game work out which spells the character is entitled to, and it would
forget the ones it had just been given.

## Comparing a screen that puts itself away

The inventory lists are drawn as far right as their longest line allows, so one
character more in one description moves the whole column. That makes the layout
worth comparing character by character - and awkward to compare, because the
command mode saves the screen on the way in and puts it back on the way out, so
by the time it returns there is nothing left to look at.

Both sides therefore dump twice: the screen as the mode left it, which proves
the restore worked, and then each list drawn again on its own, which is what
actually pins the column. Every other variation turns the weights on, since that
narrows the room left for the descriptions and moves everything.

The mode is also driven the way the main loop drives it. A key that costs a turn
makes it return with a note of where it was, and the loop calls it again next
turn; the harness does the same, up to four rounds, and prints the pack after
each. That is what covers the resuming half of the state machine rather than
just the drawing.

One thing had to be turned off to compare anything at all: bell() in io.c writes
the bell character straight to file descriptor 1, unbuffered, which is the same
stream the dump goes to - so a single mistyped key in a script put a stray byte
somewhere in the middle of the output. Turning the beep off is a player option
the real game already has, and it leaves everything else bell() does alone.

## Comparing what is overwritten as it is said

The look describes one thing at a time and writes each description over the last,
so by the time it finishes there is nothing left on the screen to compare. The
final message says only whether anything was seen at all, and the look draws no
random numbers, so the generator proves nothing either.

Both sides therefore log the message line and the cursor position every time the
look stops to ask for a key. That trace is the whole test: it says which squares
the cone reached, in what order, and what was said about each. Twenty to thirty
lines for a look in all directions, and they have to match one for one.

The arrangement is built rather than found, too. A level as generated is mostly
corridor, and a corridor's walls are all granite - which the look passes over in
silence unless something is in it - so a look down one finds nothing and proves
nothing. Both sides clear a patch of floor, put an object two and four squares
out in each of the eight directions, set mineral veins five out, and place a
creature. The same trick makes the digging and bashing comparable: whether there
is rubble next to the player is otherwise a matter of luck.

## Scripts that have to know the price

A shop is haggled with, not bought from, and an offer only means anything if it
is near the price. The price depends on the item, the shopkeeper's race, their
opinion of the player and the player's charisma - so a script of fixed numbers
either overshoots every time or insults the shopkeeper every time, and proves
nothing either way. The first set of scripts here did exactly that: they matched
perfectly and never once closed a deal.

So the offers are written as markers - what the shopkeeper opens at, the most
the player would pay, the middle of the two - and each side fills them in with
its own arithmetic just before typing them. The filled script is printed as part
of the dump. That makes the test stronger rather than weaker: a disagreement
about what something is worth shows up as two different scripts and a diff that
starts at the first line.

It was exactly that which found the pricing bug. AIrom's item_value() decided
whether the player recognised something by looking only at the store-bought
flag on the item, never at what the player had actually learned - so a potion
they had identified was still priced as a mystery. Shop stock is store-bought by
definition, so the older shops mode could not see it; only selling something of
the player's own does.

## A cursor that does not follow the text

The look's key trace records where the cursor was each time the game stopped to
ask, and that caught a difference nothing else had: after the monster memory
wrote a page, the two sides disagreed about where the cursor had been left.

Umoria writes through curses' mvaddstr, and the honest answer is that the cursor
ends up at the *start* of what was written, not the end - move() puts it there
and writing a string does not carry it along. Only addch, a single character,
takes the cursor with it. AIrom's Display was not moving the cursor at all when
it wrote, so it kept whatever position the last explicit move had set.

That is now mirrored: writing a line or erasing one parks the cursor where the
text starts, and drawing a single character leaves it one further on. It matters
because the caret is something the player can see, and because the trace is only
worth keeping if every part of it is real.

## Two more files the harness can compile

help.c was on the excluded list from the beginning, along with io.c, death.c,
signals.c and files.c - the files that needed a terminal or a Unix kernel. That
was true of it once, but ident_char() only draws and reads keys, and both of
those the fake curses has provided since io.c itself was brought in. It is now
compiled, and the symbol table is compared rather than reimplemented.

wizard.c was never excluded; it simply had nothing calling it. Its three
commands are now driven the way a player drives them - a run of typed answers,
including the ones that back out part-way, since backing out of any question in
the character editor abandons every question after it.

One thing had to be allowed for. The item builder announces itself before its
first question, and writing that question over the message line flushes the
announcement through a -more- that takes a key with it. Every one of those
scripts therefore opens with a space that is eaten before the real answers
start; without it the whole script slides by one and quietly builds the wrong
thing.

## Reaching inside generate.c and main.c

Everything in `generate.c` is `static` except `generate_cave()`, so linking
against it allows comparing whole finished levels and nothing smaller. That is
no use while the port is being built a layer at a time.

`oracle_probe.c` solves it by `#include`-ing the source rather than linking it,
which reaches the file's statics while leaving the reference tree untouched —
no patched copy to reconcile later. `generate.c` is excluded from the build's
file list precisely because it arrives through the probe instead.

`dungeon.c` and `moria3.c` arrive the same way, through
`oracle_probe_dungeon.c` and `oracle_probe_moria3.c`. Each needs a translation
unit of its own: the 1989 headers have no include guards, so pulling two game
sources into one file redefines every struct in them.

`oracle_probe_main.c` does the same for `main.c`, whose static `init_t_level()`
builds the depth-sorted object index. That file also defines the game's own
`main()`, so the probe renames it before including. Reimplementing the sort in
the harness instead would only have proved that two copies written for this
project agreed — not that either matched Umoria.

## The recording curses

`io.c` is no longer excluded. It is compiled unchanged against a curses
replacement in `fake_curses.c` that draws into a 24×80 grid instead of a
terminal, plus a handful of placeholder Unix headers in `fakeunix/`.

That makes the composed screen as diffable as everything else — which matters
most for the panel arithmetic, where a dungeon coordinate becomes a screen one.
Reimplementing `io.c` inside the harness would only have compared two copies
written for this project.

`inkey()` reads through `getch()`, so scripting the fake `getch` drives the real
input path rather than bypassing it.

Some screens are torn down before they can be dumped — `screen_map` draws the
whole level, waits for a key, then puts back what was there before. Arming a
snapshot captures the screen at the moment it asks, which is the only point the
drawing exists.

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

## The last two files, and the one that is not a port

death.c and files.c came in together, since neither is much use without the
other: the gravestone offers to write the character out, and writing the
character out is files.c.

Three headers had to be faked before death.c would compile - `pwd.h`, and the
`L_SET` and `LOCK_*` constants of `sys/file.h` - and `flock` stubbed to succeed,
since there is nobody to lock against. That was expected. What was not is that
the harness segfaulted on the tomb. `date()` does this:

```c
long clockvar;
clockvar = time((long *)0);
return ctime(&clockvar);
```

On the LLP64 model that Windows uses, `time_t` is eight bytes and `long` is
four, so `ctime` is handed a pointer to four bytes and reads eight. The build
now compiles with `-Dctime=oracle_ctime`, which returns a fixed date - which the
comparison wanted anyway, since two runs at two different moments could never
agree on today's date. AIrom's side pins the same date by overriding one method.

The tomb also caught a real porting mistake that no unit test would have. Its
prompt has two forms in the C, and the one that reads

```c
if (get_check("Save character record?"))
```

is inside `#ifdef MAC`. The portable form is a single `get_string` where a file
name writes the record out, an empty answer shows it on the screen, and an
escape leaves without either - and that is what the port had to be rewritten to
do. The four answers are each a variation of the `death` mode.

One quirk of the harness is worth recording, because it cost an hour. The tomb
script kept running out of keys. The reason was that filling the character in
prints messages of its own - "You can learn some new prayers now." - and a
waiting message turns the first prompt into a `-more-` that eats the first
scripted key. Every mode that scripts a prompt now clears `msg_flag`
immediately before feeding its keys, rather than once at the top.

The score *table* is the one thing here that was rewritten rather than ported,
so it is the one thing the oracle does not compare: Umoria's is a setuid file
shared between every player on a Unix machine, and AIrom's is a plain file
belonging to one player on one machine. The record inside it is still the
original's, and `score` compares it byte for byte - every byte of the XOR chain,
written and read back. Unit tests hold up the table around it.

## Current verification

The C oracle builds with gcc 16.1.0 (MSYS2 UCRT64) and `rng` matches AIrom
exactly: **20,000 draws across seven seeds — 140,000 values** — including the
boundaries 0, 1, `M-1` and `UINT_MAX`, and the folded start state and final
state in each.

`death`, `sheet` and `score` match across **178 runs**: the gravestone in all
four of its answers, the crowning of a winner, the character sheet on the screen
and the same sheet written out to a file, and 226 bytes of score record per run
compared one byte at a time.

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
