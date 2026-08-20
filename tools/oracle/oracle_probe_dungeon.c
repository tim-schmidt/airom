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
