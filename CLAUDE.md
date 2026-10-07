# AIrom

A C# port of Umoria 5.6 for Windows, macOS and Linux. The 1989 C original lives
at `c:\code\moria` and is **reference only — never modify it**.

One game on every platform, and no platform branches. The game reaches the
machine only through what .NET already makes portable - `System.Console`,
`PosixSignalRegistration`, `Environment.SpecialFolder` - so there is nothing to
branch on. Where the original has an `#ifdef`, the port took the branch Windows
(or DOS) would take and says so in a comment; that choice holds on a Mac or
Linux too, so all three play the same game and read the same savefile.

## Building and running

```
dotnet publish src/Airom -c Release
```

That is the whole of it, and it builds for the machine it runs on, putting the
game here:

```
src\Airom\bin\Release\net10.0\<rid>\publish\airom[.exe]
```

where `<rid>` is `win-x64`, `osx-arm64`, `linux-x64` and so on. The game is
compiled with NativeAOT, which links with the platform's own toolchain, so a
machine builds only for itself. On Windows that needs the Visual Studio Build
Tools with the C++ workload; if the link step reports `'vswhere.exe' is not
recognized`, the folder `Visual Studio\Installer` under Program Files (x86) is
missing from PATH. Release downloads for every platform are built by the
Release workflow, `.github/workflows/release.yml`.

**Publish there and nowhere else.** It is tempting to pass `-o` and put a copy
somewhere convenient - the oracle wants a path to an executable, and a scratch
directory reads like the tidier answer. It is not. Publishing to a scratch copy
leaves the default directory holding an older build, and the game a player
launches is whichever one they happen to have a shortcut to. That has already
happened once: two commits' worth of fixes were compared, verified and reported
as working while the binary in the default directory was three hours stale.

One build. Point the oracle at it, rather than building a second one for the
oracle to look at.

```
dotnet test tests/Airom.Tests
```

## The oracle

`tools/oracle/` builds the original C headless so both sides can print the same
plain-text dump and be diffed. It is the closest thing this port has to a
specification. `tools/oracle/README.md` is the full account; the short form:

```
CC=/c/msys64/ucrt64/bin/gcc.exe bash tools/oracle/build.sh   # build the C side
bash tools/oracle/compare.sh <mode> <args>                   # one comparison
bash tools/oracle/regress.sh                                 # all of them
```

The oracle is built and run on Windows, and stays there. Windows is LLP64, with
a 32-bit `long`; macOS and Linux are LP64, with a 64-bit one, and 1989 C that
assumed the first can disagree with itself on the second for reasons that have
nothing to do with the port. The C# side is platform-free managed code, so a
comparison made on Windows holds for every build.

Both take the game's path from `$AIROM`. Without it, `compare.sh` falls back to
`dotnet run` - always current, and slow enough that `regress.sh` instead defaults
to the publish directory above. So publish first, then run the sweep.

Run the full sweep before committing anything that touches shared state. It is
728 comparisons and takes a few minutes.

## Two rules the harness earned the hard way

Both are written up at length in `tools/oracle/README.md`, and both were paid
for with bugs that reached a player.

**A harness may arrange the world, but never set a value the code under test
derives.** Say what the player is holding; let the game say what that amounts
to. Setting `player_light` directly hid a lantern that lit nothing for as long
as the port existed, and every mode was green throughout.

**What is not printed is not compared.** A mode that dumps a screen and not the
cursor is not comparing the cursor. Two player-found bugs lived in exactly that
gap.

There is a third, learned since: **a harness must arrange a world the game can
actually reach.** An empty equipment slot in Umoria is `invcopy(ptr,
OBJ_NOTHING)` - a row of the object table, not a zeroed struct. The harness left
those slots at the zeros a C global starts life with, the port zeroed them to
match, and the `save` mode then held the port to a state no player could ever be
in. When a comparison fails, check which side is wrong before assuming it is the
port.

## Faithful quirks

`FAITHFUL QUIRK` comments mark oddities reproduced on purpose - the `-more-`
that eats a scripted key, a message shown twice, a `reset_seed` that does not
land where it started. They are a backlog to be revisited together in a later
improvement phase, not fixed one at a time as they turn up.

## Player files

`MORIA_SAV` and `MORIA_TOP` override the savefile and the score file. Anything
that runs the game outside a real session must set both, or it writes over the
player's own.
