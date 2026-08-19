/* Reaches inside generate.c.

   Everything in Umoria's generate.c is static except generate_cave(), so an
   oracle that only links against it can compare finished levels and nothing
   smaller. That is no use while the port is being built a layer at a time:
   the first thing worth checking is whether the streamer carver agrees, long
   before rooms or monsters exist.

   Including the source rather than linking it gives access to the file's
   statics while leaving the reference tree untouched - no patched copy, no
   edits to compare against later. generate.c is excluded from the build's file
   list precisely because it arrives here instead.

   Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
   Copyright (C) 2026 AIrom contributors
   Licensed under the GNU General Public License v3 or later. See LICENSE. */

#include "generate.c"

void probe_blank_cave(void)
{
  blank_cave();
}

void probe_fill_cave(int fval)
{
  fill_cave(fval);
}

void probe_place_boundary(void)
{
  place_boundary();
}

void probe_place_streamer(int fval, int treas_chance)
{
  place_streamer(fval, treas_chance);
}

void probe_tlink(void)
{
  tlink();
}

void probe_mlink(void)
{
  mlink();
}

void probe_build_room(int yval, int xval)
{
  build_room(yval, xval);
}

void probe_build_type1(int yval, int xval)
{
  build_type1(yval, xval);
}

void probe_build_tunnel(int row1, int col1, int row2, int col2)
{
  build_tunnel(row1, col1, row2, col2);
}

void probe_reset_doors(void)
{
  doorindex = 0;
}

/* cave_gen revisits every junction a tunnel recorded and decides which become
   doors. Replaying that loop needs the stack itself, not just its size. */
int probe_door_count(void)
{
  return doorindex;
}

void probe_door_at(int i, int *y, int *x)
{
  *y = doorstk[i].y;
  *x = doorstk[i].x;
}

void probe_try_door(int y, int x)
{
  try_door(y, x);
}
