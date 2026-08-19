/* Headless reference harness for Umoria 5.6.

   Umoria has no test suite, and "correct" for AIrom means "does what the C
   did". This program is the other half of that comparison: it drives the
   original code from a fixed seed and prints the result as plain text, so the
   same run against the port can be diffed line for line.

   Everything the game generates - dungeon layout, monster placement, item
   drops, the town - comes off one Park-Miller generator, so an identical dump
   means the whole chain that produced it agrees.

   Usage:
       oracle rng   <seed> <count>    raw generator values
       oracle seeds <seed>            the seeding chain and magic_init
       oracle cave  <seed> <level>    a generated dungeon level

   Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
   Copyright (C) 2026 AIrom contributors
   Licensed under the GNU General Public License v3 or later. See LICENSE. */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
#endif

#include "config.h"
#include "constant.h"
#include "types.h"
#include "externs.h"

/* Bump when the dump format changes in a way the C# side must match. */
#define ORACLE_FORMAT 1

/* desc.c defines this but externs.h never declares it, so the harness has to
   name it itself. The width matches the definition in desc.c. */
extern char titles[MAX_TITLES][10];

/* Implemented in oracle_probe.c, which #includes generate.c to reach its
   statics. See that file for why. */
extern void probe_blank_cave(void);
extern void probe_fill_cave(int fval);
extern void probe_place_boundary(void);
extern void probe_place_streamer(int fval, int treas_chance);
extern void probe_tlink(void);
extern void probe_mlink(void);
extern void probe_build_room(int yval, int xval);
extern void probe_build_type1(int yval, int xval);
extern void probe_build_tunnel(int row1, int col1, int row2, int col2);
extern void probe_reset_doors(void);
extern int probe_door_count(void);
extern void probe_door_at(int i, int *y, int *x);
extern void probe_try_door(int y, int x);
extern void probe_place_stairs(int typ, int num, int walls);
extern void probe_new_spot(int *y, int *x);
extern void probe_alloc_object(int which_set, int typ, int num);

/* From oracle_probe_main.c, which reaches the object sort inside main.c. */
extern void probe_init_t_level(void);

/* Windows stdio opens stdout in text mode and rewrites every "\n" as "\r\n",
   which would make all output differ from the C# side on line endings alone.
   The dump is defined as bare LF, so put the stream in binary mode.

   This is the harness, not the game, so a platform guard is fine here - it
   keeps the oracle buildable on a Unix box without pulling in Windows headers
   that do not exist there. */
static void use_unix_line_endings(void)
{
#ifdef _WIN32
  _setmode(_fileno(stdout), _O_BINARY);
#endif
}

static void header(const char *mode, unsigned long seed)
{
  printf("# airom-oracle %d\n", ORACLE_FORMAT);
  printf("mode %s\n", mode);
  printf("seed %lu\n", seed);
}

/* ------------------------------------------------------------------- rng */

/* The generator on its own: set_rnd_seed() then a run of rnd(). This is the
   narrowest check there is, and the one that must pass before any other
   comparison means anything. */
static void dump_rng(unsigned long seed, long count)
{
  long i;

  header("rng", seed);
  printf("count %ld\n", count);

  set_rnd_seed((int32u)seed);
  printf("state-after-set-seed %lu\n", (unsigned long)get_rnd_seed());

  for (i = 1; i <= count; i++)
    {
      printf("value %ld %ld\n", i, (long)rnd());
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ----------------------------------------------------------------- seeds */

/* The seeding chain the game actually uses. init_seeds() derives the object
   appearance seed and the town seed from one value, then burns a random number
   of draws; magic_init() then shuffles appearances inside a set_seed/reset_seed
   bracket.

   That bracket is the interesting part. reset_seed() restores the saved state
   through set_rnd_seed(), which adds one - so the main sequence does not come
   back where it left off. AIrom reproduces that deliberately, and this mode is
   how that is confirmed against the original rather than assumed. */
static void dump_seeds(unsigned long seed)
{
  int i;

  header("seeds", seed);

  init_seeds((int32u)seed);
  printf("randes-seed %lu\n", (unsigned long)randes_seed);
  printf("town-seed %lu\n", (unsigned long)town_seed);
  printf("state-after-init-seeds %lu\n", (unsigned long)get_rnd_seed());

  magic_init();
  printf("state-after-magic-init %lu\n", (unsigned long)get_rnd_seed());

  /* The shuffled appearance tables are themselves derived state, so dumping
     them catches a divergence inside magic_init rather than only after it. */
  for (i = 0; i < MAX_COLORS; i++)
    {
      printf("color %d %s\n", i, colors[i]);
    }
  for (i = 0; i < MAX_WOODS; i++)
    {
      printf("wood %d %s\n", i, woods[i]);
    }
  for (i = 0; i < MAX_METALS; i++)
    {
      printf("metal %d %s\n", i, metals[i]);
    }
  for (i = 0; i < MAX_ROCKS; i++)
    {
      printf("rock %d %s\n", i, rocks[i]);
    }
  for (i = 0; i < MAX_AMULETS; i++)
    {
      printf("amulet %d %s\n", i, amulets[i]);
    }
  for (i = 0; i < MAX_MUSH; i++)
    {
      printf("mushroom %d %s\n", i, mushrooms[i]);
    }
  for (i = 0; i < MAX_TITLES; i++)
    {
      printf("title %d %s\n", i, titles[i]);
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------------ cave */

/* Dungeon generation reads a few pieces of player state - depth reached and
   character level feed the monster and object allocators - so they are pinned
   here rather than left at whatever the globals happen to hold. This is a
   deliberately minimal character, not a generated one: player_birth() is
   interactive and would drag the whole creation flow in. */
static void pin_player(int level)
{
  memset((char *)&py, 0, sizeof(py));

  (void)strcpy(py.misc.name, "Oracle");
  py.misc.male = TRUE;
  py.misc.lev = 1;
  py.misc.max_dlv = (int16u)level;
  py.misc.mhp = 10;
  py.misc.chp = 10;
  py.misc.prace = 0;
  py.misc.pclass = 0;

  char_row = -1;
  char_col = -1;

  /* Turn drives the town's day/night cycle, so it has to be fixed too. */
  turn = 0;
}

static char feature_char(int fval)
{
  /* One character per cave value, chosen so a dumped level is readable at a
     glance while still being exact - every distinct fval maps to a distinct
     character. */
  switch (fval)
    {
    case NULL_WALL:     return ' ';
    case DARK_FLOOR:    return '.';
    case LIGHT_FLOOR:   return ',';
    case CORR_FLOOR:    return '#';
    case BLOCKED_FLOOR: return '%';
    case TMP1_WALL:     return '1';
    case TMP2_WALL:     return '2';
    case GRANITE_WALL:  return 'G';
    case MAGMA_WALL:    return 'M';
    case QUARTZ_WALL:   return 'Q';
    case BOUNDARY_WALL: return 'B';
    default:            return '?';
    }
}

static void dump_cave(unsigned long seed, int level)
{
  int i, j;
  char *row;

  header("cave", seed);
  printf("level %d\n", level);

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);

  dun_level = (int16)level;
  generate_cave();

  printf("height %d\n", (int)cur_height);
  printf("width %d\n", (int)cur_width);
  printf("char-row %d\n", (int)char_row);
  printf("char-col %d\n", (int)char_col);

  row = (char *)malloc((size_t)cur_width + 1);
  if (row == NULL)
    {
      fprintf(stderr, "oracle: out of memory\n");
      exit(2);
    }

  for (i = 0; i < cur_height; i++)
    {
      for (j = 0; j < cur_width; j++)
        {
          row[j] = feature_char((int)cave[i][j].fval);
        }
      row[cur_width] = '\0';
      printf("row %d %s\n", i, row);
    }

  /* Lighting and field-mark flags are separate from the terrain, and a
     generator bug can get the layout right while getting these wrong. */
  for (i = 0; i < cur_height; i++)
    {
      for (j = 0; j < cur_width; j++)
        {
          cave_type *c = &cave[i][j];
          row[j] = (char)('0'
                          + (c->lr ? 1 : 0)
                          + (c->fm ? 2 : 0)
                          + (c->pl ? 4 : 0));
        }
      row[cur_width] = '\0';
      printf("flags %d %s\n", i, row);
    }

  free(row);

  for (i = MIN_MONIX; i < mfptr; i++)
    {
      monster_type *m = &m_list[i];
      printf("monster %d %d %d %d %d %d %d\n",
             i, (int)m->fy, (int)m->fx, (int)m->mptr,
             (int)m->hp, (int)m->cspeed, (int)m->csleep);
    }

  for (i = MIN_TRIX; i < tcptr; i++)
    {
      inven_type *t = &t_list[i];
      printf("object %d %d %d %d %d %ld\n",
             i, (int)t->index, (int)t->tval, (int)t->subval,
             (int)t->number, (long)t->cost);
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------- streamers */

/* The terrain primitives on their own: blank the cave, fill it with granite,
   drive the mineral veins through it, then wall the edges.

   This is not a playable level - no rooms, no tunnels - but it exercises
   fill_cave, place_streamer, place_gold and place_boundary against a known
   generator state, which is exactly the layer of the port being built. Rooms
   and tunnels get their own mode once they exist. */
static void dump_streamers(unsigned long seed, int level)
{
  int i, j;
  char *row;

  header("streamers", seed);
  printf("level %d\n", level);

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  probe_tlink();
  probe_mlink();
  probe_blank_cave();

  cur_height = MAX_HEIGHT;
  cur_width = MAX_WIDTH;

  probe_fill_cave(GRANITE_WALL);
  for (i = 0; i < DUN_STR_MAG; i++)
    {
      probe_place_streamer(MAGMA_WALL, DUN_STR_MC);
    }
  for (i = 0; i < DUN_STR_QUA; i++)
    {
      probe_place_streamer(QUARTZ_WALL, DUN_STR_QC);
    }
  probe_place_boundary();

  printf("height %d\n", (int)cur_height);
  printf("width %d\n", (int)cur_width);

  row = (char *)malloc((size_t)cur_width + 1);
  if (row == NULL)
    {
      fprintf(stderr, "oracle: out of memory\n");
      exit(2);
    }

  for (i = 0; i < cur_height; i++)
    {
      for (j = 0; j < cur_width; j++)
        {
          row[j] = feature_char((int)cave[i][j].fval);
        }
      row[cur_width] = '\0';
      printf("row %d %s\n", i, row);
    }

  free(row);

  /* The gold dropped along the veins, in list order, then where each landed. */
  printf("objects %d\n", (int)(tcptr - MIN_TRIX));
  for (i = MIN_TRIX; i < tcptr; i++)
    {
      inven_type *t = &t_list[i];
      printf("object %d %d %d %d %ld\n",
             i, (int)t->index, (int)t->tval, (int)t->subval, (long)t->cost);
    }

  for (i = 0; i < cur_height; i++)
    {
      for (j = 0; j < cur_width; j++)
        {
          if (cave[i][j].tptr != 0)
            {
              printf("at %d %d %d\n", i, j, (int)cave[i][j].tptr);
            }
        }
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ----------------------------------------------------------------- rooms */

/* One room builder, exercised over the whole grid of room slots cave_gen
   would use.

   Rooms are placed at the same coordinates the real generator picks - the room
   grid is spaced half a screen apart - so the builders see the same kind of
   positions they will in a finished level, while the choice of which builder
   runs stays fixed instead of being drawn. That keeps the comparison pointed at
   one function at a time.

   Type 0 is build_room, the plain rectangle. Type 1 is build_type1, two or
   three overlapping rectangles. */
static void dump_rooms(unsigned long seed, int level, int type)
{
  int i, j, k;
  char *row;

  header("rooms", seed);
  printf("level %d\n", level);
  printf("type %d\n", type);

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  probe_tlink();
  probe_mlink();
  probe_blank_cave();

  cur_height = MAX_HEIGHT;
  cur_width = MAX_WIDTH;

  for (i = 0; i < 2 * (cur_height / SCREEN_HEIGHT); i++)
    {
      for (j = 0; j < 2 * (cur_width / SCREEN_WIDTH); j++)
        {
          int yloc = i * (SCREEN_HEIGHT >> 1) + QUART_HEIGHT;
          int xloc = j * (SCREEN_WIDTH >> 1) + QUART_WIDTH;

          if (type == 0)
            {
              probe_build_room(yloc, xloc);
            }
          else
            {
              probe_build_type1(yloc, xloc);
            }
        }
    }

  probe_fill_cave(GRANITE_WALL);
  probe_place_boundary();

  printf("height %d\n", (int)cur_height);
  printf("width %d\n", (int)cur_width);

  row = (char *)malloc((size_t)cur_width + 1);
  if (row == NULL)
    {
      fprintf(stderr, "oracle: out of memory\n");
      exit(2);
    }

  for (i = 0; i < cur_height; i++)
    {
      for (j = 0; j < cur_width; j++)
        {
          row[j] = feature_char((int)cave[i][j].fval);
        }
      row[cur_width] = '\0';
      printf("row %d %s\n", i, row);
    }

  /* lr marks a square as part of a room, which the builders set alongside the
     terrain. Getting the shape right while getting this wrong would leave rooms
     that never light up. */
  for (i = 0; i < cur_height; i++)
    {
      k = 0;
      for (j = 0; j < cur_width; j++)
        {
          row[j] = cave[i][j].lr ? 'L' : '.';
          if (cave[i][j].lr)
            {
              k++;
            }
        }
      row[cur_width] = '\0';
      if (k > 0)
        {
          printf("lit %d %s\n", i, row);
        }
    }

  free(row);

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* --------------------------------------------------------------- tunnels */

/* Rooms, then corridors joining them, then the junction doors.

   This is cave_gen's own order minus the streamers and stairs, which keeps the
   comparison on the tunneller. Rooms are built with build_room at fixed
   coordinates and in fixed order - no shuffle - so which rooms get joined is
   not itself drawn from the generator, and a divergence points at the tunnel
   code rather than at the order it ran in. */
static void dump_tunnels(unsigned long seed, int level)
{
  int i, j, k, rows, cols, count;
  int yloc[64], xloc[64];
  char *row;

  header("tunnels", seed);
  printf("level %d\n", level);

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  probe_tlink();
  probe_mlink();
  probe_blank_cave();

  cur_height = MAX_HEIGHT;
  cur_width = MAX_WIDTH;

  rows = 2 * (cur_height / SCREEN_HEIGHT);
  cols = 2 * (cur_width / SCREEN_WIDTH);

  count = 0;
  for (i = 0; i < rows; i++)
    {
      for (j = 0; j < cols; j++)
        {
          yloc[count] = i * (SCREEN_HEIGHT >> 1) + QUART_HEIGHT;
          xloc[count] = j * (SCREEN_WIDTH >> 1) + QUART_WIDTH;
          probe_build_room(yloc[count], xloc[count]);
          count++;
        }
    }

  probe_reset_doors();

  /* Join each room to the next, wrapping back to the first, as cave_gen does. */
  yloc[count] = yloc[0];
  xloc[count] = xloc[0];
  for (i = 0; i < count; i++)
    {
      probe_build_tunnel(yloc[i + 1], xloc[i + 1], yloc[i], xloc[i]);
    }

  probe_fill_cave(GRANITE_WALL);
  probe_place_boundary();

  printf("junctions %d\n", probe_door_count());
  k = probe_door_count();
  for (i = 0; i < k; i++)
    {
      int dy, dx;
      probe_door_at(i, &dy, &dx);
      printf("junction %d %d %d\n", i, dy, dx);
      probe_try_door(dy, dx - 1);
      probe_try_door(dy, dx + 1);
      probe_try_door(dy - 1, dx);
      probe_try_door(dy + 1, dx);
    }

  printf("height %d\n", (int)cur_height);
  printf("width %d\n", (int)cur_width);

  row = (char *)malloc((size_t)cur_width + 1);
  if (row == NULL)
    {
      fprintf(stderr, "oracle: out of memory\n");
      exit(2);
    }

  for (i = 0; i < cur_height; i++)
    {
      for (j = 0; j < cur_width; j++)
        {
          row[j] = feature_char((int)cave[i][j].fval);
        }
      row[cur_width] = '\0';
      printf("row %d %s\n", i, row);
    }

  free(row);

  /* Every door the tunneller left, with the p1 that separates locked from
     stuck from broken. */
  printf("objects %d\n", (int)(tcptr - MIN_TRIX));
  for (i = MIN_TRIX; i < tcptr; i++)
    {
      inven_type *t = &t_list[i];
      printf("object %d %d %d %d\n", i, (int)t->index, (int)t->tval, (int)t->p1);
    }

  for (i = 0; i < cur_height; i++)
    {
      for (j = 0; j < cur_width; j++)
        {
          if (cave[i][j].tptr != 0)
            {
              printf("at %d %d %d\n", i, j, (int)cave[i][j].tptr);
            }
        }
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ---------------------------------------------------------------- stairs */

/* The whole terrain half of cave_gen: rooms, corridors, junction doors,
   granite fill, mineral veins, boundary, then staircases and the spot the
   player starts on.

   This is cave_gen exactly, stopping short of alloc_monster and alloc_object -
   everything that shapes the map, none of what populates it. Rooms are still
   built in fixed order with build_room so the comparison stays pointed at the
   terrain code rather than at which room type was drawn. */
static void dump_stairs(unsigned long seed, int level)
{
  int i, j, k, rows, cols, count, alloc_level, cy, cx;
  int yloc[64], xloc[64];
  char *row;

  header("stairs", seed);
  printf("level %d\n", level);

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  probe_tlink();
  probe_mlink();
  probe_blank_cave();

  cur_height = MAX_HEIGHT;
  cur_width = MAX_WIDTH;

  rows = 2 * (cur_height / SCREEN_HEIGHT);
  cols = 2 * (cur_width / SCREEN_WIDTH);

  count = 0;
  for (i = 0; i < rows; i++)
    {
      for (j = 0; j < cols; j++)
        {
          yloc[count] = i * (SCREEN_HEIGHT >> 1) + QUART_HEIGHT;
          xloc[count] = j * (SCREEN_WIDTH >> 1) + QUART_WIDTH;
          probe_build_room(yloc[count], xloc[count]);
          count++;
        }
    }

  probe_reset_doors();
  yloc[count] = yloc[0];
  xloc[count] = xloc[0];
  for (i = 0; i < count; i++)
    {
      probe_build_tunnel(yloc[i + 1], xloc[i + 1], yloc[i], xloc[i]);
    }

  probe_fill_cave(GRANITE_WALL);
  for (i = 0; i < DUN_STR_MAG; i++)
    {
      probe_place_streamer(MAGMA_WALL, DUN_STR_MC);
    }
  for (i = 0; i < DUN_STR_QUA; i++)
    {
      probe_place_streamer(QUARTZ_WALL, DUN_STR_QC);
    }
  probe_place_boundary();

  k = probe_door_count();
  for (i = 0; i < k; i++)
    {
      int dy, dx;
      probe_door_at(i, &dy, &dx);
      probe_try_door(dy, dx - 1);
      probe_try_door(dy, dx + 1);
      probe_try_door(dy - 1, dx);
      probe_try_door(dy + 1, dx);
    }

  alloc_level = dun_level / 3;
  if (alloc_level < 2)
    alloc_level = 2;
  else if (alloc_level > 10)
    alloc_level = 10;
  printf("alloc-level %d\n", alloc_level);

  probe_place_stairs(2, randint(2) + 2, 3);
  probe_place_stairs(1, randint(2), 3);

  probe_new_spot(&cy, &cx);
  printf("char-row %d\n", cy);
  printf("char-col %d\n", cx);

  printf("height %d\n", (int)cur_height);
  printf("width %d\n", (int)cur_width);

  row = (char *)malloc((size_t)cur_width + 1);
  if (row == NULL)
    {
      fprintf(stderr, "oracle: out of memory\n");
      exit(2);
    }

  for (i = 0; i < cur_height; i++)
    {
      for (j = 0; j < cur_width; j++)
        {
          row[j] = feature_char((int)cave[i][j].fval);
        }
      row[cur_width] = '\0';
      printf("row %d %s\n", i, row);
    }

  free(row);

  printf("objects %d\n", (int)(tcptr - MIN_TRIX));
  for (i = MIN_TRIX; i < tcptr; i++)
    {
      inven_type *t = &t_list[i];
      printf("object %d %d %d %d\n", i, (int)t->index, (int)t->tval, (int)t->p1);
    }

  for (i = 0; i < cur_height; i++)
    {
      for (j = 0; j < cur_width; j++)
        {
          if (cave[i][j].tptr != 0)
            {
              printf("at %d %d %d\n", i, j, (int)cave[i][j].tptr);
            }
        }
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ----------------------------------------------------------------- picks */

/* The object index and the draws that read it.

   get_obj_num picks items out of a table sorted by depth, so this dumps the
   sort itself and then a run of picks at the given level - both with and
   without the "must fit in a chest" restriction, since that path rejects and
   redraws.

   No enchantment happens here: magic_treasure is a separate layer. What is
   compared is which object was chosen, not what it was turned into. */
static void dump_picks(unsigned long seed, int level, int count)
{
  int i;

  header("picks", seed);
  printf("level %d\n", level);
  printf("count %d\n", count);

  probe_init_t_level();

  printf("max-obj-level %d\n", MAX_OBJ_LEVEL);
  printf("dungeon-objects %d\n", MAX_DUNGEON_OBJ);

  for (i = 0; i <= MAX_OBJ_LEVEL; i++)
    {
      printf("t-level %d %d\n", i, (int)t_level[i]);
    }

  for (i = 0; i < MAX_DUNGEON_OBJ; i++)
    {
      printf("sorted %d %d\n", i, (int)sorted_objects[i]);
    }

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  for (i = 0; i < count; i++)
    {
      int any = get_obj_num(level, FALSE);
      int small = get_obj_num(level, TRUE);
      printf("pick %d %d %d %d %d\n",
             i, any, (int)sorted_objects[any], small, (int)sorted_objects[small]);
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------- enchanted */

/* Objects generated and enchanted at a given depth.

   magic_treasure is the largest function in Umoria and almost every branch
   ends in a different combination of bonuses, flags, charges and price, so the
   dump reports the whole item rather than a summary. Running it over many
   items at several depths reaches most of the switch: the chance of any magic
   at all, of something special, and of a curse all move with depth. */
static void dump_enchanted(unsigned long seed, int level, int count)
{
  int i, cur_pos;

  header("enchanted", seed);
  printf("level %d\n", level);
  printf("count %d\n", count);

  probe_init_t_level();

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  probe_tlink();

  for (i = 0; i < count; i++)
    {
      inven_type *t;
      int pick;

      /* popt would run the list out; reuse one slot instead, since each item
         is fully overwritten by invcopy before being enchanted. */
      cur_pos = MIN_TRIX;
      pick = get_obj_num(level, FALSE);
      invcopy(&t_list[cur_pos], sorted_objects[pick]);
      magic_treasure(cur_pos, level);

      t = &t_list[cur_pos];
      printf("item %d %d %d %d %d %ld %d %d %d %d %d %d %d %lu\n",
             i,
             (int)sorted_objects[pick],
             (int)t->tval,
             (int)t->subval,
             (int)t->p1,
             (long)t->cost,
             (int)t->number,
             (int)t->weight,
             (int)t->tohit,
             (int)t->todam,
             (int)t->ac,
             (int)t->toac,
             (int)t->level,
             (unsigned long)t->flags);
      printf("item-extra %d %d %d\n", i, (int)t->name2, (int)t->ident);
    }

  printf("missile-counter %d\n", (int)missile_ctr);
  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* -------------------------------------------------------------- populate */

/* A finished level, less the monsters.

   This is cave_gen from end to end apart from alloc_monster and
   place_win_monster, which are not ported yet. Skipping them on both sides
   keeps the generator streams aligned, so what is compared is every object
   scattered across a real level: rubble in the corridors, treasure in the
   rooms, gold and traps anywhere. */
static void dump_populate(unsigned long seed, int level)
{
  int i, j, k, rows, cols, count, alloc_level, cy, cx;
  int yloc[64], xloc[64];
  char *row;

  header("populate", seed);
  printf("level %d\n", level);

  probe_init_t_level();

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  probe_tlink();
  probe_mlink();
  probe_blank_cave();

  cur_height = MAX_HEIGHT;
  cur_width = MAX_WIDTH;

  rows = 2 * (cur_height / SCREEN_HEIGHT);
  cols = 2 * (cur_width / SCREEN_WIDTH);

  count = 0;
  for (i = 0; i < rows; i++)
    {
      for (j = 0; j < cols; j++)
        {
          yloc[count] = i * (SCREEN_HEIGHT >> 1) + QUART_HEIGHT;
          xloc[count] = j * (SCREEN_WIDTH >> 1) + QUART_WIDTH;
          probe_build_room(yloc[count], xloc[count]);
          count++;
        }
    }

  probe_reset_doors();
  yloc[count] = yloc[0];
  xloc[count] = xloc[0];
  for (i = 0; i < count; i++)
    {
      probe_build_tunnel(yloc[i + 1], xloc[i + 1], yloc[i], xloc[i]);
    }

  probe_fill_cave(GRANITE_WALL);
  for (i = 0; i < DUN_STR_MAG; i++)
    probe_place_streamer(MAGMA_WALL, DUN_STR_MC);
  for (i = 0; i < DUN_STR_QUA; i++)
    probe_place_streamer(QUARTZ_WALL, DUN_STR_QC);
  probe_place_boundary();

  k = probe_door_count();
  for (i = 0; i < k; i++)
    {
      int dy, dx;
      probe_door_at(i, &dy, &dx);
      probe_try_door(dy, dx - 1);
      probe_try_door(dy, dx + 1);
      probe_try_door(dy - 1, dx);
      probe_try_door(dy + 1, dx);
    }

  alloc_level = dun_level / 3;
  if (alloc_level < 2)
    alloc_level = 2;
  else if (alloc_level > 10)
    alloc_level = 10;

  probe_place_stairs(2, randint(2) + 2, 3);
  probe_place_stairs(1, randint(2), 3);

  probe_new_spot(&cy, &cx);
  char_row = (int16)cy;
  char_col = (int16)cx;
  printf("char-row %d\n", cy);
  printf("char-col %d\n", cx);

  /* alloc_monster would run here; it is not ported yet and is skipped on both
     sides so the streams stay aligned. */

  probe_alloc_object(0, 3, randint(alloc_level));
  probe_alloc_object(1, 5, randnor(TREAS_ROOM_ALLOC, 3));
  probe_alloc_object(2, 5, randnor(TREAS_ANY_ALLOC, 3));
  probe_alloc_object(2, 4, randnor(TREAS_GOLD_ALLOC, 3));
  probe_alloc_object(2, 1, randint(alloc_level));

  printf("height %d\n", (int)cur_height);
  printf("width %d\n", (int)cur_width);

  row = (char *)malloc((size_t)cur_width + 1);
  if (row == NULL)
    {
      fprintf(stderr, "oracle: out of memory\n");
      exit(2);
    }

  for (i = 0; i < cur_height; i++)
    {
      for (j = 0; j < cur_width; j++)
        row[j] = feature_char((int)cave[i][j].fval);
      row[cur_width] = '\0';
      printf("row %d %s\n", i, row);
    }

  free(row);

  printf("objects %d\n", (int)(tcptr - MIN_TRIX));
  for (i = MIN_TRIX; i < tcptr; i++)
    {
      inven_type *t = &t_list[i];
      printf("object %d %d %d %d %d %ld %d %d %d %lu %d\n",
             i, (int)t->index, (int)t->tval, (int)t->subval, (int)t->p1,
             (long)t->cost, (int)t->number, (int)t->tohit, (int)t->todam,
             (unsigned long)t->flags, (int)t->name2);
    }

  for (i = 0; i < cur_height; i++)
    for (j = 0; j < cur_width; j++)
      if (cave[i][j].tptr != 0)
        printf("at %d %d %d\n", i, j, (int)cave[i][j].tptr);

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ---------------------------------------------------------------- driver */

static int usage(void)
{
  fprintf(stderr,
          "usage:\n"
          "  oracle rng   <seed> <count>   raw generator values\n"
          "  oracle seeds <seed>           seeding chain and magic_init\n"
          "  oracle cave  <seed> <level>   a generated dungeon level\n"
          "  oracle streamers <seed> <level>  terrain primitives only\n"
          "  oracle rooms <seed> <level> <type>  one room builder\n"
          "  oracle tunnels <seed> <level>  rooms joined by corridors\n"
          "  oracle stairs <seed> <level>  the whole terrain half of cave_gen\n"
          "  oracle picks <seed> <level> <count>  object sort and get_obj_num\n"
          "  oracle enchanted <seed> <level> <count>  magic_treasure\n"
          "  oracle populate <seed> <level>  a finished level, less monsters\n");
  return 2;
}

int main(int argc, char *argv[])
{
  use_unix_line_endings();

  if (argc < 3)
    {
      return usage();
    }

  if (strcmp(argv[1], "rng") == 0)
    {
      if (argc != 4)
        {
          return usage();
        }
      dump_rng(strtoul(argv[2], NULL, 10), strtol(argv[3], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "seeds") == 0)
    {
      if (argc != 3)
        {
          return usage();
        }
      dump_seeds(strtoul(argv[2], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "streamers") == 0)
    {
      if (argc != 4)
        {
          return usage();
        }
      dump_streamers(strtoul(argv[2], NULL, 10), (int)strtol(argv[3], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "rooms") == 0)
    {
      if (argc != 5)
        {
          return usage();
        }
      dump_rooms(strtoul(argv[2], NULL, 10),
                 (int)strtol(argv[3], NULL, 10),
                 (int)strtol(argv[4], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "tunnels") == 0)
    {
      if (argc != 4)
        {
          return usage();
        }
      dump_tunnels(strtoul(argv[2], NULL, 10), (int)strtol(argv[3], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "stairs") == 0)
    {
      if (argc != 4)
        {
          return usage();
        }
      dump_stairs(strtoul(argv[2], NULL, 10), (int)strtol(argv[3], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "picks") == 0)
    {
      if (argc != 5)
        {
          return usage();
        }
      dump_picks(strtoul(argv[2], NULL, 10),
                 (int)strtol(argv[3], NULL, 10),
                 (int)strtol(argv[4], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "enchanted") == 0)
    {
      if (argc != 5)
        {
          return usage();
        }
      dump_enchanted(strtoul(argv[2], NULL, 10),
                     (int)strtol(argv[3], NULL, 10),
                     (int)strtol(argv[4], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "populate") == 0)
    {
      if (argc != 4)
        {
          return usage();
        }
      dump_populate(strtoul(argv[2], NULL, 10), (int)strtol(argv[3], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "cave") == 0)
    {
      if (argc != 4)
        {
          return usage();
        }
      dump_cave(strtoul(argv[2], NULL, 10), (int)strtol(argv[3], NULL, 10));
      return 0;
    }

  return usage();
}
