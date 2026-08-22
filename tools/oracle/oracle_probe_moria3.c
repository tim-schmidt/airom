/* Reaches inside moria3.c.

   hit_trap() and carry() are static there, and the trap mode needs to spring a
   trap without walking the player onto it. moria3.c arrives the way generate.c
   and dungeon.c do - included rather than linked, in a translation unit of its
   own, because the 1989 headers have no include guards.

   Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
   Copyright (C) 2026 AIrom contributors
   Licensed under the GNU General Public License v3 or later. See LICENSE. */

#include "moria3.c"

void probe_hit_trap(y, x)
int y, x;
{
  hit_trap(y, x);
}

void probe_carry(y, x, pickup)
int y, x, pickup;
{
  carry(y, x, pickup);
}
