#!/usr/bin/env bash
# Diff AIrom against the Umoria 5.6 reference oracle.
#
# Both sides print the same plain-text format, so a clean diff means the whole
# chain that produced the dump agrees - the generator, the seeding, and
# eventually the dungeon generator itself.
#
# Usage:
#   tools/oracle/compare.sh rng   <seed> <count>
#   tools/oracle/compare.sh seeds <seed>
#   tools/oracle/compare.sh cave  <seed> <level>
#
# Environment:
#   ORACLE   path to the built oracle   (default: tools/oracle/oracle.exe)
#   AIROM    path to the built airom    (default: dotnet run)

set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"

ORACLE="${ORACLE:-$here/oracle.exe}"

if [[ $# -lt 1 ]]; then
    echo "usage: compare.sh <mode> <args...>" >&2
    exit 2
fi

if [[ ! -x "$ORACLE" ]]; then
    echo "error: oracle not built at $ORACLE" >&2
    echo "run tools/oracle/build.sh first" >&2
    exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "mode: $*"

if ! "$ORACLE" "$@" > "$work/reference.txt" 2> "$work/reference.err"; then
    echo "error: oracle failed" >&2
    cat "$work/reference.err" >&2
    exit 1
fi

if [[ -n "${AIROM:-}" ]]; then
    "$AIROM" oracle "$@" > "$work/airom.txt" 2> "$work/airom.err"
else
    (cd "$repo" && dotnet run --project src/Airom --verbosity quiet -- oracle "$@") \
        > "$work/airom.txt" 2> "$work/airom.err"
fi

status=$?
if [[ $status -ne 0 ]]; then
    echo "airom exited $status" >&2
    cat "$work/airom.err" >&2
    [[ $status -eq 3 ]] && echo "(that part of the port does not exist yet)" >&2
    exit $status
fi

reference_lines=$(wc -l < "$work/reference.txt")

if diff -u "$work/reference.txt" "$work/airom.txt" > "$work/diff.txt"; then
    echo "MATCH  ($reference_lines lines identical)"
    exit 0
fi

echo "DIFFER"
echo
head -40 "$work/diff.txt"

total=$(grep -c '^[+-]' "$work/diff.txt" || true)
echo
echo "$total differing lines; first divergence above"
exit 1
