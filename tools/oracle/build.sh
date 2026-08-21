#!/usr/bin/env bash
# Build the Umoria 5.6 reference oracle.
#
# Compiles the original game logic headless, leaving out the five files that
# needed a terminal or a Unix kernel - io.c, death.c, signals.c, files.c and
# help.c - and linking oracle_stubs.c in their place. Those are exactly the
# files that make Umoria awkward to build on Windows, so skipping them is what
# makes this affordable.
#
# Usage:
#   tools/oracle/build.sh [path-to-moria] [output]

set -euo pipefail

MORIA="${1:-C:/code/moria}"
OUT="${2:-$(dirname "$0")/oracle.exe}"
SRC="$MORIA/source"

if [[ ! -f "$SRC/generate.c" ]]; then
    echo "error: no Umoria sources at $SRC" >&2
    exit 1
fi

CC="${CC:-gcc}"

# An MSYS2 gcc invoked by absolute path from another shell - Git Bash, say -
# launches but cannot find its own runtime DLLs, and dies with no diagnostic at
# all. Putting the compiler's own directory on PATH is what makes that work.
if [[ "$CC" == */* && -x "$CC" ]]; then
    PATH="$(cd "$(dirname "$CC")" && pwd):$PATH"
    export PATH
fi

if ! command -v "$CC" >/dev/null 2>&1; then
    cat >&2 <<'EOF'
error: no C compiler found.

The oracle needs one to build the 1989 sources. Install MSYS2 and its
toolchain, then re-run:

    winget install MSYS2.MSYS2
    C:\msys64\usr\bin\pacman -S --noconfirm mingw-w64-ucrt-x86_64-gcc

then put C:\msys64\ucrt64\bin on PATH, or run this script with
CC=/c/msys64/ucrt64/bin/gcc.exe
EOF
    exit 1
fi

# Files replaced by oracle_stubs.c, plus the game's own main().
# generate.c, dungeon.c and moria3.c are excluded because the probes #include
# them, to reach the statics inside. Compiling both would duplicate every
# symbol.
# io.c is now compiled: the fake curses in fake_curses.c records what it draws,
# so the display is compared against the original rather than a reimplementation.
# help.c is compiled too: ident_char() only draws and reads keys, both of which
# the fake curses provides, so the symbol table can be compared rather than
# reimplemented.
EXCLUDE="main.c signals.c generate.c dungeon.c moria3.c death.c"

sources=()
for file in "$SRC"/*.c; do
    name="$(basename "$file")"
    skip=""
    for excluded in $EXCLUDE; do
        [[ "$name" == "$excluded" ]] && skip=1
    done
    [[ -n "$skip" ]] || sources+=("$file")
done

here="$(cd "$(dirname "$0")" && pwd)"
sources+=("$here/fake_curses.c" "$here/oracle_stubs.c" "$here/oracle_probe.c" "$here/oracle_probe_dungeon.c" "$here/oracle_probe_moria3.c" "$here/oracle_probe_death.c" "$here/oracle_probe_main.c" "$here/oracle_main.c")

echo "compiling ${#sources[@]} files with $CC"

# -std=gnu89 because this is K&R-era C: implicit declarations and old-style
# definitions are the norm here, not mistakes. Warnings are off for the same
# reason - the goal is a faithful build of 1989 code, not a clean one.
# oracle_shim.h is forced ahead of every file to work around the 1989
# "long time();" declarations colliding with a 64-bit time_t. See the comment
# in that header for why -D on its own cannot do it.
"$CC" -std=gnu89 -w -O1 \
    -include "$here/oracle_shim.h" \
    -I"$here/fakeunix" \
    -Dctime=oracle_ctime \
    -I"$SRC" \
    -o "$OUT" \
    "${sources[@]}"

echo "built $OUT"
"$OUT" rng 1 3 || true
