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

   localtime is renamed for the same reason and then some. check_time() in
   misc1.c holds the clock in a long and passes its address to localtime, which
   on 64-bit Windows reads eight bytes from a four-byte variable: the result is
   never a valid time, localtime hands back a null pointer, and the game
   dereferences it. The replacement takes the long the 1989 code actually has
   and returns a fixed date, which also keeps the play-hours check off the
   wall clock.

   Copyright (C) 2026 AIrom contributors
   Licensed under the GNU General Public License v3 or later. See LICENSE. */

#ifndef ORACLE_SHIM_H
#define ORACLE_SHIM_H

#include <time.h>

#define time moria_time
#define localtime moria_localtime

/* Declared here because the 1989 sources declare time() themselves but not
   localtime(): without this the K&R rules make it return int, and a pointer
   truncated to thirty-two bits is the kind of bug that reads plausible garbage
   rather than crashing. */
extern struct tm *moria_localtime();

#endif /* ORACLE_SHIM_H */
