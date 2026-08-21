/* Reaches inside dungeon.c.

   The command loop's translation and validity tables are static, as are the
   regeneration helpers, so dungeon.c arrives the same way generate.c does:
   included rather than linked. It needs a translation unit of its own because
   the 1989 headers have no include guards, so pulling two game sources into one
   file redefines every struct in them.

   Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
   Copyright (C) 2026 AIrom contributors
   Licensed under the GNU General Public License v3 or later. See LICENSE. */

#include "dungeon.c"

char probe_original_commands(command)
char command;
{
  return original_commands(command);
}

int probe_valid_countcommand(command)
char command;
{
  return valid_countcommand(command);
}

void probe_regenhp(percent)
int percent;
{
  regenhp(percent);
}

void probe_regenmana(percent)
int percent;
{
  regenmana(percent);
}

/* hit_trap is static in moria3.c, not dungeon.c, and moria3.c is linked rather
   than included - so the trap mode reaches it the way a step onto one does,
   through carry(), which move_char() calls. Standing the player on the trap and
   walking them onto their own square springs it. */

/* And the dispatch itself, which is where a command's whole meaning lives.
   Reaching it is the only way to compare what a key does rather than merely
   what it translates to. */
void probe_do_command(command)
char command;
{
  do_command(command);
}
