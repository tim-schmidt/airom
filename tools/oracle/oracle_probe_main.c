/* Reaches init_t_level() inside main.c.

   get_obj_num() picks items out of sorted_objects[] and t_level[], an index of
   the object table sorted by depth. Those are built by init_t_level(), which is
   static in main.c - and main.c is excluded from the oracle build because it
   also defines the game's own main().

   Renaming that main and including the file gives the real sort rather than a
   reimplementation of it. That distinction matters: comparing the port against
   a copy of the sort written for this harness would only prove the two copies
   agreed, not that either matched Umoria.

   Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
   Copyright (C) 2026 AIrom contributors
   Licensed under the GNU General Public License v3 or later. See LICENSE. */

#define main oracle_unused_main
#include "main.c"
#undef main

void probe_init_t_level(void)
{
  init_t_level();
}

/* The same for the creature table. get_mons_num divides by entries in
   m_level, so leaving it zeroed is not a wrong answer but a crash. */
void probe_init_m_level(void)
{
  init_m_level();
}

/* And the starting kit, which is the one other thing main.c keeps to itself. */
void probe_char_inven_init(void)
{
  char_inven_init();
}
