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
extern void probe_alloc_monster(int num, int dis, int slp);
extern void probe_build_store(int store_num, int y, int x);
extern char probe_original_commands(char command);
extern int probe_valid_countcommand(char command);
extern void probe_regenhp(int percent);
extern void probe_regenmana(int percent);

/* From oracle_probe_main.c, which reaches the object sort inside main.c. */
extern void probe_init_t_level(void);
extern void probe_init_m_level(void);
extern void oracle_feed_keys(char *keys);

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

  probe_init_t_level();
  probe_init_m_level();

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
  printf("monsters %d\n", (int)(mfptr - MIN_MONIX));
  for (i = MIN_MONIX; i < mfptr; i++)
    {
      monster_type *m = &m_list[i];
      printf("monster %d %d %d %d %d %d %d\n",
             i, (int)m->fy, (int)m->fx, (int)m->mptr,
             (int)m->hp, (int)m->cspeed, (int)m->csleep);
    }

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

  /* No monster is placed this early, but the empty block is printed all the
     same, so the two dumps line up. */
  printf("monsters %d\n", (int)(mfptr - MIN_MONIX));
  for (i = MIN_MONIX; i < mfptr; i++)
    {
      monster_type *m = &m_list[i];
      printf("monster %d %d %d %d %d %d %d\n",
             i, (int)m->fy, (int)m->fx, (int)m->mptr,
             (int)m->hp, (int)m->cspeed, (int)m->csleep);
    }

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

  printf("monsters %d\n", (int)(mfptr - MIN_MONIX));
  for (i = MIN_MONIX; i < mfptr; i++)
    {
      monster_type *m = &m_list[i];
      printf("monster %d %d %d %d %d %d %d\n",
             i, (int)m->fy, (int)m->fx, (int)m->mptr,
             (int)m->hp, (int)m->cspeed, (int)m->csleep);
    }

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
  probe_init_m_level();

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

  probe_alloc_monster(randint(8) + MIN_MALLOC_LEVEL + alloc_level, 0, TRUE);

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

  printf("monsters %d\n", (int)(mfptr - MIN_MONIX));
  for (i = MIN_MONIX; i < mfptr; i++)
    {
      monster_type *m = &m_list[i];
      printf("monster %d %d %d %d %d %d %d\n",
             i, (int)m->fy, (int)m->fx, (int)m->mptr,
             (int)m->hp, (int)m->cspeed, (int)m->csleep);
    }

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

/* ------------------------------------------------------------------ town */

/* The town, less the shop restocking.

   town_gen ends by calling store_maint, which is not ported yet, so this
   replays town_gen's body up to that point rather than calling it. Everything
   that makes the map - the six shops, their doors, the stairs, the lighting and
   the townsfolk - is compared. */
static void dump_town(unsigned long seed, long turn_count)
{
  int i, j, k, l, m, cy, cx;
  int rooms[6];
  char *row;

  header("town", seed);
  printf("turn %ld\n", turn_count);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();
  pin_player(0);
  dun_level = 0;
  turn = (int32)turn_count;

  probe_tlink();
  probe_mlink();
  probe_blank_cave();

  store_init();

  cur_height = SCREEN_HEIGHT;
  cur_width = SCREEN_WIDTH;

  set_seed(town_seed);
  for (i = 0; i < 6; i++)
    rooms[i] = i;
  l = 6;
  for (i = 0; i < 2; i++)
    for (j = 0; j < 3; j++)
      {
        k = randint(l) - 1;
        probe_build_store(rooms[k], i, j);
        for (m = k; m < l - 1; m++)
          rooms[m] = rooms[m + 1];
        l--;
      }

  probe_fill_cave(DARK_FLOOR);
  probe_place_boundary();
  probe_place_stairs(2, 1, 0);
  reset_seed();

  probe_new_spot(&cy, &cx);
  char_row = (int16)cy;
  char_col = (int16)cx;
  printf("char-row %d\n", cy);
  printf("char-col %d\n", cx);

  if (0x1 & (turn / 5000))
    {
      printf("phase night\n");
      for (i = 0; i < cur_height; i++)
        for (j = 0; j < cur_width; j++)
          if (cave[i][j].fval != DARK_FLOOR)
            cave[i][j].pl = TRUE;
      probe_alloc_monster(MIN_MALLOC_TN, 3, TRUE);
    }
  else
    {
      printf("phase day\n");
      for (i = 0; i < cur_height; i++)
        for (j = 0; j < cur_width; j++)
          cave[i][j].pl = TRUE;
      probe_alloc_monster(MIN_MALLOC_TD, 3, TRUE);
    }

  store_maint();

  for (i = 0; i < MAX_STORES; i++)
    printf("shop %d %d %d\n", i, (int)store[i].owner, (int)store[i].store_ctr);

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

  for (i = 0; i < cur_height; i++)
    {
      for (j = 0; j < cur_width; j++)
        row[j] = cave[i][j].pl ? 'L' : '.';
      row[cur_width] = '\0';
      printf("lit %d %s\n", i, row);
    }

  free(row);

  printf("monsters %d\n", (int)(mfptr - MIN_MONIX));
  for (i = MIN_MONIX; i < mfptr; i++)
    {
      monster_type *m2 = &m_list[i];
      printf("monster %d %d %d %d %d %d\n",
             i, (int)m2->fy, (int)m2->fx, (int)m2->mptr,
             (int)m2->hp, (int)m2->csleep);
    }

  printf("objects %d\n", (int)(tcptr - MIN_TRIX));
  for (i = MIN_TRIX; i < tcptr; i++)
    printf("object %d %d %d\n", i, (int)t_list[i].index, (int)t_list[i].tval);

  for (i = 0; i < cur_height; i++)
    for (j = 0; j < cur_width; j++)
      if (cave[i][j].tptr != 0)
        printf("at %d %d %d\n", i, j, (int)cave[i][j].tptr);

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ----------------------------------------------------------------- shops */

/* The six shops: their owners, their stock and their asking prices.

   store_init hands out owners and empties the shelves; store_maint then turns
   the stock over, selling some off and taking some in. Repeating the
   maintenance simulates the shops changing across several visits to town,
   which is where the interesting behaviour is - a shop that only ever filled
   up would look the same after the first pass. */
static void dump_shops(unsigned long seed, int rounds)
{
  int i, j, r;

  header("shops", seed);
  printf("rounds %d\n", rounds);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();
  pin_player(0);
  dun_level = 0;

  probe_tlink();

  store_init();

  for (i = 0; i < MAX_STORES; i++)
    printf("owner %d %d\n", i, (int)store[i].owner);

  for (r = 0; r < rounds; r++)
    {
      store_maint();
      printf("round %d state %lu\n", r, (unsigned long)get_rnd_seed());

      for (i = 0; i < MAX_STORES; i++)
        {
          store_type *s = &store[i];
          printf("store %d %d %d\n", r, i, (int)s->store_ctr);

          for (j = 0; j < s->store_ctr; j++)
            {
              inven_type *it = &s->store_inven[j].sitem;
              printf("stock %d %d %d %d %d %d %d %ld %ld %d %d %d\n",
                     r, i, j,
                     (int)it->index, (int)it->tval, (int)it->subval,
                     (int)it->number, (long)it->cost,
                     (long)s->store_inven[j].scost,
                     (int)it->p1, (int)it->name2, (int)it->ident);
            }
        }
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------- character */

/* A rolled character.

   create_character asks for race, sex and class at a prompt rather than taking
   them as arguments, so the choices are fed in as keystrokes and the real
   function runs unchanged. That reaches the whole of create.c - the stat roll
   and its re-roll band, the race and class adjustments, the history walk, age
   and build, the hit point curve and the starting money. */
static void dump_character(unsigned long seed, int race_index, int sex, int pclass)
{
  char keys[16];
  int i;

  header("character", seed);
  printf("race %d\n", race_index);
  printf("sex %d\n", sex);
  printf("class %d\n", pclass);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();

  /* get_class lists only the classes the race allows, and the letter indexes
     that list rather than the class table. Work out which letter names the
     class asked for, and say so plainly when the race cannot take it. */
  {
    int letter = -1;
    int slot = 0;
    int j;

    for (j = 0; j < MAX_CLASS; j++)
      {
        if (race[race_index].rtclass & (0x1L << j))
          {
            if (j == pclass)
              {
                letter = slot;
              }
            slot++;
          }
      }

    if (letter < 0)
      {
        printf("invalid race and class combination\n");
        return;
      }

    /* Race letter, sex letter, ESC to accept the roll, then the class letter. */
    keys[0] = (char)('a' + race_index);
    keys[1] = sex ? 'm' : 'f';
    keys[2] = ESCAPE;
    keys[3] = (char)('a' + letter);
    /* A return for the empty name, then spaces for the closing pause:
       io.c's own inkey() reads these now, so the whole input path runs. */
    keys[4] = '\r';
    keys[5] = ' ';
    keys[6] = ' ';
    keys[7] = '\0';
    oracle_feed_keys(keys);
  }

  create_character();

  printf("name %s\n", py.misc.name);
  printf("male %d\n", (int)py.misc.male);
  printf("prace %d\n", (int)py.misc.prace);
  printf("pclass %d\n", (int)py.misc.pclass);
  printf("age %d\n", (int)py.misc.age);
  printf("height %d\n", (int)py.misc.ht);
  printf("weight %d\n", (int)py.misc.wt);
  printf("social %d\n", (int)py.misc.sc);
  printf("gold %ld\n", (long)py.misc.au);
  printf("hitdie %d\n", (int)py.misc.hitdie);
  printf("mhp %d\n", (int)py.misc.mhp);
  printf("expfact %d\n", (int)py.misc.expfact);
  printf("srh %d\n", (int)py.misc.srh);
  printf("fos %d\n", (int)py.misc.fos);
  printf("bth %d\n", (int)py.misc.bth);
  printf("bthb %d\n", (int)py.misc.bthb);
  printf("stl %d\n", (int)py.misc.stl);
  printf("save %d\n", (int)py.misc.save);
  printf("disarm %d\n", (int)py.misc.disarm);
  printf("ptohit %d\n", (int)py.misc.ptohit);
  printf("ptodam %d\n", (int)py.misc.ptodam);
  printf("ptoac %d\n", (int)py.misc.ptoac);
  printf("pac %d\n", (int)py.misc.pac);
  printf("dis_th %d\n", (int)py.misc.dis_th);
  printf("dis_td %d\n", (int)py.misc.dis_td);
  printf("dis_tac %d\n", (int)py.misc.dis_tac);
  printf("dis_ac %d\n", (int)py.misc.dis_ac);
  printf("infra %d\n", (int)py.flags.see_infra);

  for (i = 0; i < 6; i++)
    printf("stat %d %d %d %d\n",
           i, (int)py.stats.max_stat[i], (int)py.stats.cur_stat[i],
           (int)py.stats.use_stat[i]);

  for (i = 0; i < 4; i++)
    printf("history %d %s\n", i, py.misc.history[i]);

  for (i = 0; i < MAX_PLAYER_LEVEL; i++)
    printf("hp %d %d\n", i, (int)player_hp[i]);

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ---------------------------------------------------------------- screen */

/* A drawn map.

   io.c is linked against the recording curses, so this compares the composed
   screen rather than the cave behind it. What that reaches is the panel
   arithmetic: prt_map walks the visible window and print() converts each
   dungeon coordinate into a screen one, which is the part most likely to be off
   by one and the hardest to see in a grid dump.

   The level is lit and marked as seen throughout, so loc_symbol has to decide a
   glyph for every square rather than returning blanks for the unexplored parts.
   That touches every terrain value, every object and every monster on the
   level. */
static void dump_screen_at(unsigned long seed, int level, int image,
                          const char *mode)
{
  int i, j;

  header(mode, seed);
  printf("level %d\n", level);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  (void)initscr();
  oracle_screen_reset();

  generate_cave();

  /* Light the level and mark it seen, so the map is drawn rather than hidden. */
  for (i = 0; i < cur_height; i++)
    for (j = 0; j < cur_width; j++)
      {
        cave[i][j].pl = TRUE;
        cave[i][j].fm = TRUE;
      }

  /* Every monster visible, so their glyphs are drawn too. */
  for (i = MIN_MONIX; i < mfptr; i++)
    m_list[i].ml = TRUE;

  /* The player occupies index 1 of the monster list. */
  cave[char_row][char_col].cptr = 1;

  (void)get_panel((int)char_row, (int)char_col, TRUE);

  printf("panel-row %d\n", panel_row);
  printf("panel-col %d\n", panel_col);
  printf("panel-row-min %d\n", panel_row_min);
  printf("panel-col-min %d\n", panel_col_min);
  printf("panel-row-prt %d\n", panel_row_prt);
  printf("panel-col-prt %d\n", panel_col_prt);
  printf("char-row %d\n", (int)char_row);
  printf("char-col %d\n", (int)char_col);

  /* Hallucination is set after the level is built, so the map is the same
     one the plain screen mode draws and only the drawing differs. */
  py.flags.image = (int16)image;

  clear_screen();
  prt_map();

  oracle_screen_dump("scr");

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

static void dump_screen(unsigned long seed, int level)
{
  dump_screen_at(seed, level, 0, "screen");
}

/* The same map, drawn by a hallucinating character.

   One square in twelve comes out as something else entirely, and both the
   roll and the character it picks come from the generator - so a map drawn
   while hallucinating has to consume exactly the same numbers on both sides,
   not merely look similar. */
static void dump_hallucinate(unsigned long seed, int level)
{
  dump_screen_at(seed, level, 5, "hallucinate");
}

/* -------------------------------------------------------------- messages */

/* The message line.

   msg_print runs two messages together when they both fit and prompts with
   -more- when they do not, so the interesting behaviour is a sequence rather
   than a single call. The screen is dumped after each one, along with the
   history ring the player can review. */
static void dump_messages(unsigned long seed)
{
  static char *lines[] = {
    "You feel a sudden chill.",
    "It bites you.",
    "You have a Scroll of Word of Recall (e) in your pack, and it glows faintly blue.",
    "The Giant White Louse breeds explosively and the whole corridor fills with them.",
    "You die."
  };
  int i;

  header("messages", seed);

  init_seeds((int32u)seed);

  (void)initscr();
  oracle_screen_reset();

  /* Spaces to answer any -more- prompt the sequence provokes. */
  oracle_feed_keys("          ");

  for (i = 0; i < 5; i++)
    {
      msg_print(lines[i]);
      printf("after %d flag %d last %d\n", i, (int)msg_flag, (int)last_msg);
      oracle_screen_dump("msg");
    }

  /* A null message flushes whatever is showing. */
  msg_print(CNIL);
  printf("after flush flag %d\n", (int)msg_flag);
  oracle_screen_dump("msg");

  for (i = 0; i < MAX_SAVE_MSG; i++)
    printf("history %d %s\n", i, old_msg[i]);

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------------- map */

/* The whole level shrunk to one screen, as the M command shows it.

   Three squares by three collapse into one, so something has to win. What
   survives the shrinking is decided by a priority table, and getting that wrong
   would quietly lose the stairs. */
static void dump_map(unsigned long seed, int level)
{
  int i, j;

  header("map", seed);
  printf("level %d\n", level);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  /* init_curses rather than initscr: screen_map saves the screen into the
     spare window, which only init_curses allocates. */
  init_curses();
  oracle_screen_reset();

  generate_cave();

  for (i = 0; i < cur_height; i++)
    for (j = 0; j < cur_width; j++)
      {
        cave[i][j].pl = TRUE;
        cave[i][j].fm = TRUE;
      }
  for (i = MIN_MONIX; i < mfptr; i++)
    m_list[i].ml = TRUE;
  cave[char_row][char_col].cptr = 1;

  (void)get_panel((int)char_row, (int)char_col, TRUE);

  /* screen_map draws the level, waits for a key, then puts back what was on
     screen before - so the map only exists while it is asking. */
  oracle_feed_keys(" ");
  oracle_snapshot_next_key();
  screen_map();
  oracle_snapshot_dump("map");

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------- statblock */

/* The status sidebar, for a rolled character under a set of conditions.

   Each condition owns a column range along the bottom two lines, so they can be
   redrawn as they come and go. The interesting parts are the ones that interact:
   weak outranks hungry, paralysis outranks resting, searching overwrites a
   repeat count, and searching is discounted from the speed before deciding
   whether there is anything to say about it. */
static void dump_statblock(unsigned long seed, int variation)
{
  char keys[16];
  int letter, slot, j;

  header("statblock", seed);
  printf("variation %d\n", variation);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();

  init_curses();
  oracle_screen_reset();

  /* A human warrior, so the character is the same in every variation. */
  letter = -1;
  slot = 0;
  for (j = 0; j < MAX_CLASS; j++)
    {
      if (race[0].rtclass & (0x1L << j))
        {
          if (j == 0)
            letter = slot;
          slot++;
        }
    }

  keys[0] = 'a';
  keys[1] = 'm';
  keys[2] = ESCAPE;
  keys[3] = (char)('a' + letter);
  keys[4] = '\r';
  keys[5] = ' ';
  keys[6] = ' ';
  keys[7] = '\0';
  oracle_feed_keys(keys);

  create_character();
  character_generated = 1;

  py.misc.exp = 12345;
  py.misc.cmana = 7;
  py.misc.dis_ac = 14;
  py.misc.au = 4321;

  switch (variation)
    {
    case 0:
      break;
    case 1:
      py.flags.status |= PY_HUNGRY;
      break;
    case 2:
      py.flags.status |= (PY_WEAK | PY_HUNGRY);
      break;
    case 3:
      py.flags.status |= (PY_BLIND | PY_CONFUSED | PY_FEAR | PY_POISONED);
      break;
    case 4:
      py.flags.status |= PY_SEARCH;
      break;
    case 5:
      py.flags.status |= PY_REST;
      py.flags.rest = 42;
      break;
    case 6:
      py.flags.status |= PY_REST;
      py.flags.rest = -1;
      break;
    case 7:
      py.flags.paralysis = 5;
      py.flags.status |= PY_REST;
      break;
    case 8:
      command_count = 17;
      break;
    case 9:
      command_count = 17;
      py.flags.status |= PY_SEARCH;
      break;
    case 10:
      py.flags.speed = 2;
      break;
    case 11:
      py.flags.speed = -3;
      break;
    case 12:
      py.flags.speed = 1;
      py.flags.status |= PY_SEARCH;
      break;
    case 13:
      py.flags.new_spells = 2;
      break;
    case 14:
      total_winner = TRUE;
      break;
    case 15:
      noscore |= 0x2;
      wizard = TRUE;
      break;
    case 16:
      /* The whole range cnv_stat has to write, including the 18/100 that is the
         only three digit remainder. */
      py.stats.use_stat[0] = 3;
      py.stats.use_stat[1] = 18;
      py.stats.use_stat[2] = 19;
      py.stats.use_stat[3] = 18 + 22;
      py.stats.use_stat[4] = 18 + 99;
      py.stats.use_stat[5] = 18 + 100;
      break;
    case 17:
      py.misc.lev = 0;
      break;
    case 18:
      py.misc.lev = MAX_PLAYER_LEVEL;
      break;
    case 19:
      py.misc.lev = MAX_PLAYER_LEVEL + 1;
      break;
    default:
      py.misc.lev = MAX_PLAYER_LEVEL + 1;
      py.misc.male = FALSE;
      break;
    }

  clear_screen();
  prt_stat_block();

  oracle_screen_dump("stat");

  /* Only the bits the sidebar itself reads or writes: the rest are requests to
     recompute something, raised here by carrying the starting inventory. */
  printf("status %lu\n",
         (unsigned long)(py.flags.status
                         & (PY_HUNGRY | PY_WEAK | PY_BLIND | PY_CONFUSED | PY_FEAR
                            | PY_POISONED | PY_SEARCH | PY_REST | PY_STUDY
                            | PY_REPEAT)));
  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ---------------------------------------------------------------- commands */

/* Writes a key the way the dump names it: printable characters as themselves,
   everything else by its numeric value, since control keys are commands here. */
static void print_key(char *label, int key)
{
  if (key > 32 && key < 127)
    printf("%s '%c'", label, key);
  else
    printf("%s %d", label, key);
}

/* The command tables: every key through the original-to-rogue translation and
   the count validity test.

   Two of the translations - walk and tunnel - read a direction before they can
   answer, because the rogue-like set spells the direction into the command
   letter. Each key is therefore tried twice: once with a direction waiting, and
   once with an escape, which is the player abandoning the command. */
static void dump_commands(void)
{
  int key;
  char keys[4];

  printf("mode commands\n");

  init_curses();
  oracle_screen_reset();

  rogue_like_commands = FALSE;
  default_dir = FALSE;
  command_count = 0;

  for (key = 0; key < 128; key++)
    {
      char answered, abandoned;

      keys[0] = '4';
      keys[1] = '\0';
      oracle_feed_keys(keys);
      free_turn_flag = FALSE;
      answered = probe_original_commands((char)key);

      keys[0] = ESCAPE;
      keys[1] = '\0';
      oracle_feed_keys(keys);
      free_turn_flag = FALSE;
      abandoned = probe_original_commands((char)key);

      print_key("key", key);
      print_key(" answered", (int)(unsigned char)answered);
      print_key(" abandoned", (int)(unsigned char)abandoned);
      printf(" count %d\n", probe_valid_countcommand((char)key) ? 1 : 0);
    }
}

/* --------------------------------------------------------------- regenerate */

/* Hit point and mana regeneration.

   Both carry a fraction in 1/65536ths between turns, because a character
   regenerates far less than a point a turn. The interesting cases are the
   carry, the clamp at full, and the overflow guard that a very high maximum
   would otherwise walk into. */
static void dump_regen(unsigned long seed, int turns)
{
  int i;

  header("regen", seed);
  printf("turns %d\n", turns);

  init_curses();
  oracle_screen_reset();

  py.misc.mhp = 250;
  py.misc.chp = 1;
  py.misc.chp_frac = 0;
  py.misc.mana = 90;
  py.misc.cmana = 0;
  py.misc.cmana_frac = 0;

  for (i = 0; i < turns; i++)
    {
      int percent;

      /* Walk the three food bands and the doubled resting rate, so every
         regeneration factor the loop can pass in is covered. */
      switch (i % 4)
        {
        case 0: percent = PLAYER_REGEN_NORMAL; break;
        case 1: percent = PLAYER_REGEN_WEAK; break;
        case 2: percent = PLAYER_REGEN_FAINT; break;
        default: percent = PLAYER_REGEN_NORMAL * 2; break;
        }

      probe_regenhp(percent);
      probe_regenmana(percent);

      printf("turn %d percent %d chp %d frac %d cmana %d frac %d\n",
             i, percent, (int)py.misc.chp, (int)py.misc.chp_frac,
             (int)py.misc.cmana, (int)py.misc.cmana_frac);
    }

  /* The overflow guard: a maximum beyond a signed short saturates rather than
     wrapping negative. */
  py.misc.mhp = MAX_SHORT;
  py.misc.chp = MAX_SHORT - 1;
  py.misc.chp_frac = 0;
  probe_regenhp(PLAYER_REGEN_NORMAL * 2);
  printf("clamped chp %d frac %d\n", (int)py.misc.chp, (int)py.misc.chp_frac);
}

/* ------------------------------------------------------------------ upkeep */

/* The turn: what happens to the player between one command and the next.

   The real dungeon() runs here, driven the only way a headless harness can
   drive it - the character is paralysed for the length of the run, so no
   command is asked for, and a quit is left in the key script for the turn the
   paralysis wears off. Every counter therefore ages through the original code
   rather than through a reimplementation of it.

   The monsters are cleared off the level first. Creature movement is not ported
   yet, and a monster taking its turn would consume random numbers on this side
   only. Nothing else about the level is touched. */
static void dump_upkeep(unsigned long seed, int turns, int variation)
{
  char keys[16];
  int i, j, letter, slot;

  header("upkeep", seed);
  printf("turns %d\n", turns);
  printf("variation %d\n", variation);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();

  init_curses();
  oracle_screen_reset();

  /* A human warrior, so the character is the same in every variation. */
  letter = -1;
  slot = 0;
  for (j = 0; j < MAX_CLASS; j++)
    {
      if (race[0].rtclass & (0x1L << j))
        {
          if (j == 0)
            letter = slot;
          slot++;
        }
    }

  keys[0] = 'a';
  keys[1] = 'm';
  keys[2] = ESCAPE;
  keys[3] = (char)('a' + letter);
  keys[4] = '\r';
  keys[5] = ' ';
  keys[6] = ' ';
  keys[7] = '\0';
  oracle_feed_keys(keys);

  create_character();
  character_generated = 1;

  dun_level = 1;
  generate_cave();

  /* Empty the monster list, and take the monsters off the map with it. */
  for (i = 0; i < MAX_HEIGHT; i++)
    for (j = 0; j < MAX_WIDTH; j++)
      cave[i][j].cptr = 0;
  mfptr = MIN_MONIX;

  /* What play_game() does after create_character(), less the starting
     inventory: the pack is not ported yet, so there is nothing to carry and no
     light to burn. */
  py.flags.food = 7500;
  py.flags.food_digested = 2;

  py.misc.mana = 20;
  py.misc.cmana = 0;
  py.misc.cmana_frac = 0;
  py.misc.chp = 3;
  py.misc.chp_frac = 0;

  switch (variation)
    {
    case 0: break;
    case 1: py.flags.hero = 5; break;
    case 2: py.flags.shero = 5; break;
    case 3: py.flags.blind = 4; break;
    case 4: py.flags.confused = 4; break;
    case 5: py.flags.afraid = 4; break;
    case 6: py.flags.poisoned = 6; break;
    case 7: py.flags.fast = 3; break;
    case 8: py.flags.slow = 3; break;
    case 9: py.flags.invuln = 3; break;
    case 10: py.flags.blessed = 3; break;
    case 11:
      py.flags.protevil = 3;
      py.flags.resist_heat = 2;
      py.flags.resist_cold = 2;
      break;
    case 12: py.flags.detect_inv = 3; break;
    case 13: py.flags.tim_infra = 3; break;
    case 14:
      /* Hallucinating, but blind with it: a blind character sees nothing, so
         the drawing never reaches the roll that scrambles a square. The map
         drawn while hallucinating is compared by the hallucinate mode, which
         does not need the lighting half of moria1.c to be ported first. */
      py.flags.image = 3;
      py.flags.blind = 99;
      break;
    case 15: py.flags.food = 1500; break;
    case 16: py.flags.food = 500; break;
    case 17: py.flags.food = 100; break;
    case 18: py.flags.food = -100; break;
    case 19: py.flags.word_recall = 3; break;
    case 20:
      py.flags.rest = 20;
      py.flags.status |= PY_REST;
      break;
    case 21: py.flags.regenerate = TRUE; break;
    case 22:
      /* Fear and heroism together: heroism cancels the fear rather than
         counting it down. */
      py.flags.afraid = 9;
      py.flags.hero = 4;
      break;
    default:
      py.flags.status |= PY_SEARCH;
      break;
    }

  /* Paralysed for the run, so no command is asked for until it wears off. */
  py.flags.paralysis = turns;

  /* Quit is ^K in the original key set, then a yes to confirm. The spaces
     around it answer any -more- the run puts up - a starving character can
     faint several times over a long run - and do nothing as commands. */
  {
    char script[2001];
    int k;

    for (k = 0; k < 2000; k++)
      script[k] = ' ';
    script[200] = CTRL('K');
    script[201] = 'y';
    script[2000] = '\0';
    oracle_feed_keys(script);
  }

  /* The map is drawn by the lighting half of moria1.c, which is not ported
     yet, so only the sidebar strip is comparable. Clearing first means what is
     left there was drawn by the loop rather than before it. */
  clear_screen();

  dungeon();

  printf("turn %ld\n", (long)turn);
  printf("status %lu\n", (unsigned long)py.flags.status);
  printf("chp %d frac %d mhp %d\n", (int)py.misc.chp, (int)py.misc.chp_frac,
         (int)py.misc.mhp);
  printf("cmana %d frac %d\n", (int)py.misc.cmana, (int)py.misc.cmana_frac);
  printf("food %d digested %d\n", (int)py.flags.food,
         (int)py.flags.food_digested);
  printf("speed %d\n", (int)py.flags.speed);
  printf("bth %d bthb %d\n", (int)py.misc.bth, (int)py.misc.bthb);
  printf("pac %d dis_ac %d\n", (int)py.misc.pac, (int)py.misc.dis_ac);
  printf("hero %d shero %d blessed %d invuln %d\n", (int)py.flags.hero,
         (int)py.flags.shero, (int)py.flags.blessed, (int)py.flags.invuln);
  printf("blind %d confused %d afraid %d poisoned %d\n", (int)py.flags.blind,
         (int)py.flags.confused, (int)py.flags.afraid, (int)py.flags.poisoned);
  printf("fast %d slow %d image %d paralysis %d\n", (int)py.flags.fast,
         (int)py.flags.slow, (int)py.flags.image, (int)py.flags.paralysis);
  printf("protevil %d heat %d cold %d\n", (int)py.flags.protevil,
         (int)py.flags.resist_heat, (int)py.flags.resist_cold);
  printf("detect_inv %d see_inv %d tim_infra %d see_infra %d\n",
         (int)py.flags.detect_inv, (int)py.flags.see_inv,
         (int)py.flags.tim_infra, (int)py.flags.see_infra);
  printf("word_recall %d dun_level %d max_dlv %d\n", (int)py.flags.word_recall,
         (int)dun_level, (int)py.misc.max_dlv);
  printf("rest %d\n", (int)py.flags.rest);
  printf("death %d died_from %s\n", (int)death, died_from);
  printf("monsters %d\n", (int)(mfptr - MIN_MONIX));

  oracle_screen_dump_columns("up", 13);

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
          "  oracle populate <seed> <level>  a finished level, less monsters\n"
          "  oracle town <seed> <turn>  the town, less shop restocking\n"
          "  oracle shops <seed> <rounds>  shop owners, stock and prices\n"
          "  oracle character <seed> <race> <sex> <class>  a rolled character\n"
          "  oracle screen <seed> <level>  the drawn map\n"
          "  oracle messages <seed>  the message line and its history\n"
          "  oracle map <seed> <level>  the whole level shrunk to one screen\n"
          "  oracle statblock <seed> <variation>  the status sidebar\n"
          "  oracle commands  the command translation and count tables\n"
          "  oracle regen <seed> <turns>  hit point and mana regeneration\n"
          "  oracle upkeep <seed> <turns> <variation>  a turn in the dungeon\n"
          "  oracle hallucinate <seed> <level>  the map drawn while hallucinating\n");
  return 2;
}

int main(int argc, char *argv[])
{
  use_unix_line_endings();

  /* Most modes take a seed, but the command tables take nothing at all. */
  if (argc < 2)
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

  if (strcmp(argv[1], "town") == 0)
    {
      if (argc != 4)
        {
          return usage();
        }
      dump_town(strtoul(argv[2], NULL, 10), strtol(argv[3], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "shops") == 0)
    {
      if (argc != 4)
        {
          return usage();
        }
      dump_shops(strtoul(argv[2], NULL, 10), (int)strtol(argv[3], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "character") == 0)
    {
      if (argc != 6)
        {
          return usage();
        }
      dump_character(strtoul(argv[2], NULL, 10),
                     (int)strtol(argv[3], NULL, 10),
                     (int)strtol(argv[4], NULL, 10),
                     (int)strtol(argv[5], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "screen") == 0)
    {
      if (argc != 4)
        {
          return usage();
        }
      dump_screen(strtoul(argv[2], NULL, 10), (int)strtol(argv[3], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "messages") == 0)
    {
      if (argc != 3)
        {
          return usage();
        }
      dump_messages(strtoul(argv[2], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "map") == 0)
    {
      if (argc != 4)
        {
          return usage();
        }
      dump_map(strtoul(argv[2], NULL, 10), (int)strtol(argv[3], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "hallucinate") == 0)
    {
      if (argc != 4)
        {
          return usage();
        }
      dump_hallucinate(strtoul(argv[2], NULL, 10), (int)strtol(argv[3], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "upkeep") == 0)
    {
      if (argc != 5)
        {
          return usage();
        }
      dump_upkeep(strtoul(argv[2], NULL, 10),
                  (int)strtol(argv[3], NULL, 10),
                  (int)strtol(argv[4], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "commands") == 0)
    {
      if (argc != 2)
        {
          return usage();
        }
      dump_commands();
      return 0;
    }

  if (strcmp(argv[1], "regen") == 0)
    {
      if (argc != 4)
        {
          return usage();
        }
      dump_regen(strtoul(argv[2], NULL, 10), (int)strtol(argv[3], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "statblock") == 0)
    {
      if (argc != 4)
        {
          return usage();
        }
      dump_statblock(strtoul(argv[2], NULL, 10), (int)strtol(argv[3], NULL, 10));
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
