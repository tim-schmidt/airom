/* Force-included ahead of every translation unit in the oracle build.

   misc1.c and save.c both carry the 1989 declaration

       long time();

   which collides with the 64-bit time_t in modern Windows headers. Renaming
   with -Dtime=... does not work: the macro is in force while <time.h> itself is
   parsed, so the system declaration gets renamed too and collides all the same.

   Including the real header here first, and only then defining the macro, gives
   the system its genuine declaration and leaves every later mention of time()
   in the game sources pointing at the replacement in oracle_stubs.c.

   time_t is untouched - it is a single preprocessing token, not time followed
   by _t.

   Copyright (C) 2026 AIrom contributors
   Licensed under the GNU General Public License v3 or later. See LICENSE. */

#ifndef ORACLE_SHIM_H
#define ORACLE_SHIM_H

#include <time.h>

#define time moria_time

#endif /* ORACLE_SHIM_H */
