#!/usr/bin/env bash
# The full differential regression: every oracle mode, several seeds each.
#
# Prints one line per failure and a tally per mode at the end.

# Where "dotnet publish src/Airom -c Release" puts the game. Defaulting to a
# scratch copy somewhere else is how a stale binary gets compared for a week
# without anyone noticing.
AIROM=${AIROM:-/c/code/airom/src/Airom/bin/Release/net10.0/win-x64/publish/airom.exe}
COMPARE=/c/code/airom/tools/oracle/compare.sh

total=0
failed=0
declare -A mode_runs
declare -A mode_fails

run() {
    local mode=$1
    local out
    out=$(AIROM="$AIROM" bash "$COMPARE" "$@" 2>&1 | sed -n 2p)
    total=$((total + 1))
    mode_runs[$mode]=$(( ${mode_runs[$mode]:-0} + 1 ))

    case "$out" in
        MATCH*) ;;
        *)
            failed=$((failed + 1))
            mode_fails[$mode]=$(( ${mode_fails[$mode]:-0} + 1 ))
            echo "FAIL  $* -> $out"
            ;;
    esac
}

SEEDS="1 7 42 999"
DEPTHS="1 10 25 40"

for s in $SEEDS; do
    # --- the generator and the seeding chain
    run rng $s 200
    run seeds $s

    # --- terrain, a layer at a time
    for l in $DEPTHS; do
        run streamers $s $l
        run tunnels $s $l
        run stairs $s $l
        run populate $s $l
        run cave $s $l
        run map $s $l
        run picks $s $l 20
        run enchanted $s $l 10
        run compact $s $l 0
        run hallucinate $s $l
    done

    for t in 0 1 2 3; do
        run rooms $s 10 $t
    done

    # --- the town and its shops
    run town $s 0
    run town $s 500
    run shops $s 0
    run shops $s 5

    # --- rolling a character, and every screen that shows one
    for r in 0 1 2 3 4 5 6 7; do
        run character $s $r 1 0
    done

    for v in 0 1 2 3 5 8 13; do
        run create $s $v
        run sheet $s $v
        run death $s $v
        run statblock $s $v
    done

    # --- the whole savefile, at four depths
    for l in 0 1 12 25; do
        run save $s $l 0
        run save $s $l 3
    done

    run score $s 0 10

    # --- every key of the dispatch, both command sets
    for rogue in 0 1; do
        for l in 0 1 12; do
            run dispatch $s $l $rogue 1 127
        done
    done

    run commands

    # --- the screen, the messages, the sidebar
    run screen $s 5
    run messages $s

    # --- a turn of the real loop, in every condition it knows
    for v in 0 5 13 18 23 24 25; do
        run upkeep $s 20 $v
    done

    run regen $s 200

    # --- moving about
    for v in 0 3 7; do
        run walk $s 5 20 $v
        run light $s 5 20 $v
        run pickup $s 5 20 $v
    done

    for d in 1 2 4 6 8 9; do
        run run $s 5 $d 0
    done

    run search $s 5 40 20
    run search $s 5 40 60

    # --- fighting, and being fought
    for c in 5 40 120 200 260; do
        run fight $s 5 $c 20
    done

    for v in 0 2 5; do
        run monsters $s 5 30 $v
    done

    run traps $s 5 0 20
    run moria4 $s 5 0
    run moria4 $s 5 3
    run look $s 5 0
    run look $s 5 9
    run look $s 5 18

    # --- everything a player can use
    run names $s 0 60

    # Each range is the stretch of the object table that holds that kind:
    # asking for a range that holds none of them compares nothing.
    run potion $s 0 60
    run potion $s 300 120
    run scroll $s 5 180 120
    run wand $s 5 269 24
    run staff $s 5 293 25
    run spell $s 5 0 31
    run prayer $s 5 0 31

    # Learning them: sixteen arrangements, the screen dumped at every prompt.
    for v in 0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15; do
        run study $s $v
    done

    run inven $s 0
    run inven $s 3
    run getitem $s 0
    run getitem $s 3
    run wizard $s 5 0
    run wizard $s 5 3

    # --- the shops, and what is known about the world
    for st in 0 1 2 3 4 5; do
        run store $s $st 0
    done

    run recall $s 0 1 140
    run recall $s 1 140 139
    run symbol $s 0 1 60
done

echo
echo "----------------------------------------------------------------"
printf "%-14s %8s %8s\n" mode runs failures

for mode in $(echo "${!mode_runs[@]}" | tr ' ' '\n' | sort); do
    printf "%-14s %8d %8d\n" "$mode" "${mode_runs[$mode]}" "${mode_fails[$mode]:-0}"
done

echo "----------------------------------------------------------------"
echo "total $total runs, $failed failures"
