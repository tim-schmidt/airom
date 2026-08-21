/* Reaches inside death.c.

   print_tomb() and kingly() are static there, and the death mode needs both
   without going through exit_game(), which ends the process. death.c arrives
   the way generate.c, dungeon.c and moria3.c do - included rather than linked,
   in a translation unit of its own, because the 1989 headers have no include
   guards.

   Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
   Copyright (C) 2026 AIrom contributors
   Licensed under the GNU General Public License v3 or later. See LICENSE. */

#include "death.c"

void probe_print_tomb()
{
  print_tomb();
}

void probe_kingly()
{
  kingly();
}
