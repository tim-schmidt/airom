// Ported from Umoria 5.6 source/rnd.c and the RNG helpers in source/misc1.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
//
// This file is part of AIrom, a derivative work of Umoria, and is distributed
// under the GNU General Public License version 3 or later. See LICENSE.

namespace Airom.Core;

/// <summary>
/// Prime modulus multiplicative linear congruential generator (Lehmer /
/// Park-Miller "minimal standard"), evaluated with Schrage's method so that
/// no intermediate product overflows 32 bits.
///
/// Reference: Stephen K. Park and Keith W. Miller, "Random Number Generators:
/// Good ones are hard to find", CACM, October 1988, vol 31, no 10, pp 1192-1201.
///
/// Full period of 2^31 - 1, producing values in 1 .. 2^31 - 1.
///
/// This reproduces Umoria's sequence exactly. Dungeon layouts, monster rolls
/// and the town map are all derived from it, so any deviation here changes the
/// game. The published conformance value (seed 1 => z[10001] == 1043618065) is
/// asserted by the test suite.
/// </summary>
public sealed class Rng
{
    private const long M = 2147483647L; // modulus, 2^31 - 1 (prime)
    private const long A = 16807L;      // multiplier
    private const long Q = 127773L;     // M div A
    private const long R = 2836L;       // M mod A

    private uint _seed;
    private uint _savedSeed;

    public Rng(uint seed = 0) => SetSeed(seed);

    /// <summary>
    /// Raw generator state, in 1 .. M-1. Mirrors get_rnd_seed().
    ///
    /// Note this is deliberately read-only: feeding a captured state back in
    /// goes through <see cref="SetSeed"/>, which does not restore it exactly.
    /// See <see cref="PopSeed"/>.
    /// </summary>
    public uint State => _seed;

    /// <summary>
    /// Reseeds the generator, folding the argument into 1 .. M-1.
    /// Mirrors set_rnd_seed().
    /// </summary>
    public void SetSeed(uint seedValue) => _seed = (uint)((seedValue % (M - 1)) + 1);

    /// <summary>
    /// Saves the current state and switches to a reproducible one, so that the
    /// town map and the object appearance shuffle can be regenerated on demand.
    /// Mirrors set_seed() in misc1.c.
    ///
    /// Like the original this holds a single slot, not a stack: nested calls
    /// lose the outer state. Umoria never nests them.
    /// </summary>
    public void PushSeed(uint seed)
    {
        _savedSeed = _seed;
        SetSeed(seed);
    }

    /// <summary>
    /// Returns to the state saved by <see cref="PushSeed"/>.
    /// Mirrors reset_seed() in misc1.c.
    ///
    /// FAITHFUL QUIRK - do not "fix" this. The original restores by calling
    /// set_rnd_seed(old_seed), which applies (x % (M-1)) + 1 to a value that is
    /// already in range. The state therefore comes back one step off: a saved
    /// state of n restores as n+1, and M-1 restores as 1. Every town or item
    /// description regeneration perturbs the main sequence this way. The game
    /// stays deterministic, but restoring exactly would diverge from Umoria and
    /// change the dungeons a given save file produces.
    /// </summary>
    public void PopSeed() => SetSeed(_savedSeed);

    /// <summary>Returns the next raw value from the set 1, 2, ..., M-1. Mirrors rnd().</summary>
    public int Next()
    {
        long high = _seed / Q;
        long low = _seed % Q;
        long test = A * low - R * high;

        // Schrage guarantees |test| < M, so a single correction restores range.
        _seed = (uint)(test > 0 ? test : test + M);
        return (int)_seed;
    }

    /// <summary>Uniform value in 1 .. maxValue inclusive. Mirrors randint().</summary>
    public int RandInt(int maxValue) => (int)(Next() % maxValue) + 1;

    /// <summary>Sum of <paramref name="count"/> dice of <paramref name="sides"/> sides. Mirrors damroll().</summary>
    public int DamRoll(int count, int sides)
    {
        int sum = 0;
        for (int i = 0; i < count; i++)
        {
            sum += RandInt(sides);
        }
        return sum;
    }

    /// <summary>Rolls a packed {dice, sides} pair as stored in the monster and item tables. Mirrors pdamroll().</summary>
    public int PDamRoll(ReadOnlySpan<byte> dicePair) => DamRoll(dicePair[0], dicePair[1]);

    /// <summary>
    /// Approximately normal deviate with the given mean and standard deviation,
    /// drawn by binary-searching a cumulative table built for SD 64.
    /// Mirrors randnor().
    /// </summary>
    public int RandNor(int mean, int standardDeviation)
    {
        int tmp = RandInt(MaxShort);

        // Off the end of the table: assign a value between 4 and 5 times SD.
        if (tmp == MaxShort)
        {
            int tail = 4 * standardDeviation + RandInt(standardDeviation);
            if (RandInt(2) == 1)
            {
                tail = -tail;
            }
            return mean + tail;
        }

        // Binary search NormalTable for the entry matching tmp; at most 8 steps.
        int low = 0;
        int index = NormalTableSize >> 1;
        int high = NormalTableSize;
        while (true)
        {
            if (NormalTable[index] == tmp || high == low + 1)
            {
                break;
            }

            if (NormalTable[index] > tmp)
            {
                high = index;
                index = low + ((index - low) >> 1);
            }
            else
            {
                low = index;
                index += (high - index) >> 1;
            }
        }

        // The search can settle one entry below the target.
        if (NormalTable[index] < tmp)
        {
            index++;
        }

        // Rescale from the table's SD of 64, rounding the halfway case up.
        int offset = ((standardDeviation * index) + (NormalTableSd >> 1)) / NormalTableSd;

        if (RandInt(2) == 1)
        {
            offset = -offset;
        }

        return mean + offset;
    }

    private const int MaxShort = 32767;
    private const int NormalTableSize = 256;
    private const int NormalTableSd = 64;

    private static readonly ushort[] NormalTable =
    [
          206,   613,  1022,  1430,  1838,  2245,  2652,  3058,
         3463,  3867,  4271,  4673,  5075,  5475,  5874,  6271,
         6667,  7061,  7454,  7845,  8234,  8621,  9006,  9389,
         9770, 10148, 10524, 10898, 11269, 11638, 12004, 12367,
        12727, 13085, 13440, 13792, 14140, 14486, 14828, 15168,
        15504, 15836, 16166, 16492, 16814, 17133, 17449, 17761,
        18069, 18374, 18675, 18972, 19266, 19556, 19842, 20124,
        20403, 20678, 20949, 21216, 21479, 21738, 21994, 22245,
        22493, 22737, 22977, 23213, 23446, 23674, 23899, 24120,
        24336, 24550, 24759, 24965, 25166, 25365, 25559, 25750,
        25937, 26120, 26300, 26476, 26649, 26818, 26983, 27146,
        27304, 27460, 27612, 27760, 27906, 28048, 28187, 28323,
        28455, 28585, 28711, 28835, 28955, 29073, 29188, 29299,
        29409, 29515, 29619, 29720, 29818, 29914, 30007, 30098,
        30186, 30272, 30356, 30437, 30516, 30593, 30668, 30740,
        30810, 30879, 30945, 31010, 31072, 31133, 31192, 31249,
        31304, 31358, 31410, 31460, 31509, 31556, 31601, 31646,
        31688, 31730, 31770, 31808, 31846, 31882, 31917, 31950,
        31983, 32014, 32044, 32074, 32102, 32129, 32155, 32180,
        32205, 32228, 32251, 32273, 32294, 32314, 32333, 32352,
        32370, 32387, 32404, 32420, 32435, 32450, 32464, 32477,
        32490, 32503, 32515, 32526, 32537, 32548, 32558, 32568,
        32577, 32586, 32595, 32603, 32611, 32618, 32625, 32632,
        32639, 32645, 32651, 32657, 32662, 32667, 32672, 32677,
        32682, 32686, 32690, 32694, 32698, 32702, 32705, 32708,
        32711, 32714, 32717, 32720, 32722, 32725, 32727, 32729,
        32731, 32733, 32735, 32737, 32739, 32740, 32742, 32743,
        32745, 32746, 32747, 32748, 32749, 32750, 32751, 32752,
        32753, 32754, 32755, 32756, 32757, 32757, 32758, 32758,
        32759, 32760, 32760, 32761, 32761, 32761, 32762, 32762,
        32763, 32763, 32763, 32764, 32764, 32764, 32764, 32765,
        32765, 32765, 32765, 32766, 32766, 32766, 32766, 32766,
    ];
}
