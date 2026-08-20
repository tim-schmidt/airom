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
extern void probe_hit_trap(int y, int x);
extern void probe_carry(int y, int x, int pickup);
extern const char *oracle_screen_row(int row);
extern void oracle_log_keys(int on);

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
    case 14: py.flags.image = 3; break;
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
    case 23:
      py.flags.status |= PY_SEARCH;
      break;
    case 24:
      /* A lit lamp with plenty of oil: the player carries their own light, so
         every step lights the squares around them. */
      inventory[INVEN_LIGHT].p1 = 400;
      player_light = TRUE;
      break;
    default:
      /* A lamp about to run dry: it warns while it lasts, then goes out. */
      inventory[INVEN_LIGHT].p1 = 12;
      player_light = TRUE;
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

  /* Cleared so that what is left on the screen was drawn by the loop rather
     than by the character creation before it. */
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

  oracle_screen_dump("up");

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------------- light */

/* The eight directions, in the order the walk tries them. Numbered as the
   number pad is, so the table reads the same on both sides. */
static int light_dir_row[8] = { 0, 1, 0, -1, 1, 1, -1, -1 };
static int light_dir_col[8] = { 1, 0, -1, 0, 1, -1, 1, -1 };

/* The lighting: what the player can see, one step at a time.

   The player is walked along a fixed path - each step takes the first direction
   that is not a wall - and the screen is dumped after every one. That exercises
   the parts a stationary character never reaches: the block behind the player
   going dark, walls becoming permanently known, objects being noticed as the
   light passes over them, and a room lighting as its doorway is stepped into.

   move_char() belongs to moria2.c and is not ported, so the walk is done here:
   the square is picked, the player record moved, and the lighting told about it,
   which is the sequence move_char() itself uses. */
static void dump_light(unsigned long seed, int level, int steps, int variation)
{
  int i, j, step;

  header("light", seed);
  printf("level %d\n", level);
  printf("steps %d\n", steps);
  printf("variation %d\n", variation);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  init_curses();
  oracle_screen_reset();

  generate_cave();

  /* The monsters are cleared off: creature movement is not ported, and one
     taking its turn would consume random numbers on one side only. */
  for (i = 0; i < MAX_HEIGHT; i++)
    for (j = 0; j < MAX_WIDTH; j++)
      cave[i][j].cptr = 0;
  mfptr = MIN_MONIX;

  cave[char_row][char_col].cptr = 1;

  switch (variation)
    {
    case 0:
      /* A lit lamp: the ordinary case, where the player carries their light. */
      player_light = TRUE;
      break;
    case 1:
      /* Blind: nothing new is revealed, so only the player symbol moves. */
      player_light = TRUE;
      py.flags.blind = 500;
      break;
    case 2:
      /* No light at all, which is the same path as blindness. */
      player_light = FALSE;
      break;
    default:
      /* Running: the lamp is switched off, so a long run does not repaint the
         same nine squares at every step. */
      player_light = TRUE;
      find_flag = TRUE;
      break;
    }

  panel_row = panel_col = -1;
  check_view();

  for (step = 0; step < steps; step++)
    {
      int y = (int)char_row;
      int x = (int)char_col;
      int ny = y;
      int nx = x;

      for (i = 0; i < 8; i++)
        {
          int ty = y + light_dir_row[(step + i) % 8];
          int tx = x + light_dir_col[(step + i) % 8];

          if (cave[ty][tx].fval <= MAX_OPEN_SPACE)
            {
              ny = ty;
              nx = tx;
              break;
            }
        }

      move_rec(y, x, ny, nx);
      char_row = (int16)ny;
      char_col = (int16)nx;

      if (get_panel(ny, nx, FALSE))
        prt_map();

      move_light(y, x, ny, nx);

      printf("step %d at %d %d\n", step, ny, nx);
    }

  check_view();
  oracle_screen_dump("lit");

  /* What the player now knows about the level, square by square, so a
     difference in the flags shows even where the screen agrees. */
  {
    int permanent = 0;
    int temporary = 0;
    int marked = 0;

    for (i = 0; i < cur_height; i++)
      for (j = 0; j < cur_width; j++)
        {
          if (cave[i][j].pl)
            permanent++;
          if (cave[i][j].tl)
            temporary++;
          if (cave[i][j].fm)
            marked++;
        }

    printf("permanent %d temporary %d marked %d\n", permanent, temporary, marked);
  }

  printf("light-flag %d player-light %d\n", light_flag ? 1 : 0,
         player_light ? 1 : 0);
  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* -------------------------------------------------------------------- walk */

/* The directions a scripted walk tries, in order. Numbered as the number pad
   is, so the table reads the same on both sides. */
static int walk_script[8] = { 6, 2, 4, 8, 3, 1, 9, 7 };

/* Sets up a level for a walk: the monsters and the loose objects are taken off
   it first.

   Monsters would move on the C side only, since creature.c is not ported.
   Objects would be picked up on the C side only, since carry() needs the
   inventory - and picking one up prints a message and changes the pack, which
   is exactly the kind of divergence that would drown out what is being
   compared. Doors and stairs go with them, being objects here too. */
static void strip_level(void)
{
  int i, j;

  for (i = 0; i < MAX_HEIGHT; i++)
    for (j = 0; j < MAX_WIDTH; j++)
      {
        cave[i][j].cptr = 0;
        cave[i][j].tptr = 0;
      }

  mfptr = MIN_MONIX;
  tcptr = MIN_TRIX;
}

/* Walking, one step at a time.

   move_char() is the whole of a step: it moves the record, drags the light
   after it, lights a room on entry, searches what is nearby, and decides
   whether a wall blocks the way. The script walks a fixed cycle of directions
   rather than choosing cleverly, so both sides walk into the same walls. */
static void dump_walk(unsigned long seed, int level, int steps, int variation)
{
  int step;

  header("walk", seed);
  printf("level %d\n", level);
  printf("steps %d\n", steps);
  printf("variation %d\n", variation);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  init_curses();
  oracle_screen_reset();

  generate_cave();
  strip_level();

  cave[char_row][char_col].cptr = 1;

  /* A searcher good enough to roll for something every step, so the search
     inside move_char() is exercised rather than skipped. */
  py.misc.fos = 1;
  py.misc.srh = 40;

  switch (variation)
    {
    case 0:
      player_light = TRUE;
      break;
    case 1:
      /* Confused: three steps in four go somewhere else entirely, which draws
         random numbers of its own. */
      player_light = TRUE;
      py.flags.confused = 30000;
      break;
    case 2:
      player_light = TRUE;
      py.flags.blind = 30000;
      break;
    default:
      player_light = FALSE;
      break;
    }

  panel_row = panel_col = -1;
  check_view();

  for (step = 0; step < steps; step++)
    {
      free_turn_flag = FALSE;
      move_char(walk_script[step % 8], TRUE);
      printf("step %d at %d %d free %d\n", step, (int)char_row,
             (int)char_col, free_turn_flag ? 1 : 0);
    }

  oracle_screen_dump("walk");

  {
    int permanent = 0;
    int temporary = 0;
    int marked = 0;
    int i, j;

    for (i = 0; i < cur_height; i++)
      for (j = 0; j < cur_width; j++)
        {
          if (cave[i][j].pl)
            permanent++;
          if (cave[i][j].tl)
            temporary++;
          if (cave[i][j].fm)
            marked++;
        }

    printf("permanent %d temporary %d marked %d\n", permanent, temporary,
           marked);
  }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* Running: a step repeated until something worth stopping for turns up.

   find_init() decides what kind of run this is from the two squares either
   side of the first step, and every step after that asks area_affect() where to
   go next. The path is dumped square by square, so a run that turns one corner
   differently shows up immediately rather than only in the final position. */
static void dump_run(unsigned long seed, int level, int direction, int variation)
{
  int guard;

  header("run", seed);
  printf("level %d\n", level);
  printf("direction %d\n", direction);
  printf("variation %d\n", variation);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  init_curses();
  oracle_screen_reset();

  generate_cave();
  strip_level();

  cave[char_row][char_col].cptr = 1;

  /* No searching while running: it would draw a random number a step and drown
     the run itself in noise. */
  py.misc.fos = 30000;
  py.misc.srh = 0;

  player_light = TRUE;

  switch (variation)
    {
    case 0:
      break;
    case 1:
      /* Never cut a corner: go the long way round instead. */
      find_cut = FALSE;
      break;
    case 2:
      /* Stop at anything that might be a corner rather than examining it. */
      find_examine = FALSE;
      break;
    default:
      /* Draw the player while running, which keeps the lamp lit. */
      find_prself = TRUE;
      break;
    }

  panel_row = panel_col = -1;
  check_view();

  printf("start %d %d\n", (int)char_row, (int)char_col);

  find_init(direction);

  for (guard = 0; guard < 300 && find_flag; guard++)
    {
      printf("at %d %d\n", (int)char_row, (int)char_col);
      find_run();
    }

  printf("stopped %d %d after %d\n", (int)char_row, (int)char_col, guard);

  oracle_screen_dump("run");

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());

  find_cut = TRUE;
  find_examine = TRUE;
  find_prself = FALSE;
}

/* ------------------------------------------------------------------ search */

/* Searching for what is hidden.

   The walk mode strips the level of objects, so nothing there is ever found.
   Here the opposite: the eight squares around the player are filled with the
   things a search can turn up - invisible traps of every kind, secret doors and
   a trapped chest - and the search is run over and over.

   Every square is rolled for separately, so what is found and in which order is
   entirely the generator's doing. */
static void dump_search(unsigned long seed, int level, int rounds, int chance)
{
  int i, j, round, k;

  header("search", seed);
  printf("level %d\n", level);
  printf("rounds %d\n", rounds);
  printf("chance %d\n", chance);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  init_curses();
  oracle_screen_reset();

  generate_cave();
  strip_level();

  cave[char_row][char_col].cptr = 1;

  /* Ring the player with things to find. The traps walk the whole trap list,
     so their names are compared as well as the finding of them. */
  k = 0;
  for (i = char_row - 1; i <= char_row + 1; i++)
    for (j = char_col - 1; j <= char_col + 1; j++)
      {
        int slot;

        if (i == char_row && j == char_col)
          continue;

        slot = popt();
        if (k == 7)
          {
            /* A chest, which is found differently: the trap on it is
               discovered rather than the chest itself. */
            invcopy(&t_list[slot], OBJ_OPEN_DOOR);
            t_list[slot].tval = TV_CHEST;
            t_list[slot].flags = CH_LOSE_STR | CH_POISON;
          }
        else if (k == 6)
          invcopy(&t_list[slot], OBJ_SECRET_DOOR);
        else
          invcopy(&t_list[slot], OBJ_TRAP_LIST + k);

        cave[i][j].tptr = (int8u)slot;
        k++;
      }

  /* Spaces to answer any -more- the run of messages puts up: a good searcher
     finds several things in a round, and the message line only holds two. */
  {
    char script[2001];
    int n;

    for (n = 0; n < 2000; n++)
      script[n] = ' ';
    script[2000] = ' ';
    oracle_feed_keys(script);
  }

  for (round = 0; round < rounds; round++)
    {
      search((int)char_row, (int)char_col, chance);
      printf("round %d\n", round);

      k = 0;
      for (i = char_row - 1; i <= char_row + 1; i++)
        for (j = char_col - 1; j <= char_col + 1; j++)
          {
            inven_type *t;

            if (i == char_row && j == char_col)
              continue;

            t = &t_list[cave[i][j].tptr];
            printf("  found %d tval %d index %d ident %d\n", k,
                   (int)t->tval, (int)t->index, (int)t->ident);
            k++;
          }
    }

  oracle_screen_dump("srch");

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------------- names */

/* Naming things.

   objdes() builds every item name in the game, and what it can say depends on
   what the player knows: an unidentified wand is named by its metal, the same
   wand once identified by what it does, and either may carry a count, an
   article, dice, bonuses, charges and a brace of guesses at the end.

   Every object in the table is named four ways - unknown, kind known,
   enchantment known, both - and again as a pile rather than one, so the
   pluralising and the article are covered too. The scrolls are the reason this
   has to run after magic_init(): their titles are made of shuffled syllables. */
static void dump_names(unsigned long seed, int first, int count)
{
  int i, variation;
  bigvtype description;
  inven_type item;

  header("names", seed);
  printf("first %d\n", first);
  printf("count %d\n", count);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();

  init_curses();
  oracle_screen_reset();

  for (i = first; i < first + count && i < MAX_OBJECTS; i++)
    {
      for (variation = 0; variation < 8; variation++)
        {
          int16 offset;

          invcopy(&item, i);

          /* Something worth printing in every field: a pile of four, an
             enchantment, plusses and a name that only shows once identified. */
          if (variation & 4)
            {
              item.number = 4;
              item.tohit = 7;
              item.todam = -3;
              item.toac = 2;
              item.p1 = 5;
              item.name2 = SN_SD;
              item.ident |= ID_MAGIK;
            }

          /* Forget everything about this kind, then learn back what the
             variation calls for. */
          if ((offset = object_offset(&item)) >= 0)
            {
              int slot = (offset << 6) + (item.subval & (ITEM_SINGLE_STACK_MIN - 1));
              object_ident[slot] = 0;
              if (variation & 1)
                object_ident[slot] |= OD_KNOWN1;
              else
                object_ident[slot] |= OD_TRIED;
            }

          if (variation & 2)
            item.ident |= ID_KNOWN2;

          objdes(description, &item, TRUE);
          printf("%d %d full %s\n", i, variation, description);

          objdes(description, &item, FALSE);
          printf("%d %d bare %s\n", i, variation, description);
        }
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------------ pickup */

/* Walking over things and picking them up.

   The walk mode strips the level bare; this one leaves the objects where they
   fell, so every step onto one runs carry(): the gold into the purse, the rest
   into the pack, with the weight and the sorting that follow.

   The traps are taken off first. hit_trap() belongs to moria3.c and is not
   ported, so a trap would fire on the C side only. */
static void strip_traps(void)
{
  int i, j;

  for (i = 0; i < MAX_HEIGHT; i++)
    for (j = 0; j < MAX_WIDTH; j++)
      {
        cave[i][j].cptr = 0;

        if (cave[i][j].tptr != 0)
          {
            int t = t_list[cave[i][j].tptr].tval;

            if (t == TV_INVIS_TRAP || t == TV_VIS_TRAP || t == TV_STORE_DOOR)
              cave[i][j].tptr = 0;
          }
      }

  mfptr = MIN_MONIX;
}

static void dump_pickup(unsigned long seed, int level, int steps, int variation)
{
  int step, i;
  bigvtype description;

  header("pickup", seed);
  printf("level %d\n", level);
  printf("steps %d\n", steps);
  printf("variation %d\n", variation);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  init_curses();
  oracle_screen_reset();

  generate_cave();
  strip_traps();

  cave[char_row][char_col].cptr = 1;

  /* A scripted walk wanders in a small circle, so what it finds is left to
     chance. Ring the player with things instead: gold, a weapon, armour, a
     potion, a scroll, food, a wand, and a pile of pebbles that has to stack. */
  {
    static int ring[8] = { 399, 74, 91, 222, 173, 163, 293, 82 };
    int i, j, k = 0;

    for (i = char_row - 1; i <= char_row + 1; i++)
      for (j = char_col - 1; j <= char_col + 1; j++)
        {
          int slot;

          if (i == char_row && j == char_col)
            continue;
          if (cave[i][j].fval > MAX_OPEN_SPACE)
            {
              k++;
              continue;
            }

          slot = popt();
          invcopy(&t_list[slot], ring[k]);
          if (ring[k] == 399)
            t_list[slot].cost = 250;
          if (ring[k] == 82)
            t_list[slot].number = 12;
          cave[i][j].tptr = (int8u)slot;
          k++;
        }
  }

  /* Strong enough to carry a good deal, so the weight limit is reached by
     picking things up rather than by starting encumbered. */
  py.stats.cur_stat[A_STR] = 16;
  py.stats.max_stat[A_STR] = 16;
  py.stats.use_stat[A_STR] = 16;
  py.misc.wt = 150;
  py.misc.fos = 1;
  py.misc.srh = 40;
  player_light = TRUE;

  switch (variation)
    {
    case 0:
      break;
    case 1:
      /* Ask before picking anything up, and answer yes to everything. */
      prompt_carry_flag = TRUE;
      break;
    case 2:
      /* A weakling, who reaches the weight limit almost at once. */
      py.stats.cur_stat[A_STR] = 3;
      py.stats.max_stat[A_STR] = 3;
      py.stats.use_stat[A_STR] = 3;
      py.misc.wt = 80;
      break;
    default:
      /* Walk over everything without picking any of it up. */
      break;
    }

  /* Spaces answer the -more- prompts and the pickup questions alike; a "y"
     would be needed for a no, and yes is what these variations want. */
  {
    char script[2001];
    int n;

    for (n = 0; n < 2000; n++)
      script[n] = (n % 2) ? 'y' : ' ';
    script[2000] = '\0';
    oracle_feed_keys(script);
  }

  panel_row = panel_col = -1;
  check_view();

  for (step = 0; step < steps; step++)
    {
      free_turn_flag = FALSE;
      move_char(walk_script[step % 8], variation == 3 ? FALSE : TRUE);
      printf("step %d at %d %d free %d\n", step, (int)char_row,
             (int)char_col, free_turn_flag ? 1 : 0);
    }

  printf("gold %ld weight %d count %d burden %d\n", (long)py.misc.au,
         inven_weight, inven_ctr, pack_heavy);

  for (i = 0; i < inven_ctr; i++)
    {
      objdes(description, &inventory[i], TRUE);
      printf("pack %d %d %d %s\n", i, (int)inventory[i].number,
             (int)inventory[i].weight, description);
    }

  oracle_screen_dump("pick");

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());

  prompt_carry_flag = FALSE;
}

/* ------------------------------------------------------------------- fight */

/* Hitting things until they stop moving.

   A monster is put beside the player and attacked over and over. Every blow is
   three rolls - whether it lands, how hard, and whether it was good enough to
   count for extra - and a kill runs monster_death() as well, which rolls again
   for what was being carried. All of it is dumped, along with the experience,
   the pack and the monster memory the fight wrote. */
static void dump_fight(unsigned long seed, int level, int creature, int rounds)
{
  int round, i;
  bigvtype description;

  /* Said rather than crashed: the creature table has no bounds check of its
     own, and reading past it takes the whole run down with a signal that says
     nothing about which argument was wrong. */
  if (creature < 0 || creature >= MAX_CREATURES)
    {
      fprintf(stderr, "oracle: creature %d is outside 0..%d\n", creature,
              MAX_CREATURES - 1);
      exit(2);
    }

  header("fight", seed);
  printf("level %d\n", level);
  printf("creature %d\n", creature);
  printf("rounds %d\n", rounds);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();
  dun_level = (int16)level;

  init_curses();
  oracle_screen_reset();

  /* A rolled character rather than a pinned one: the class level tables, the
     stats and the hit dice all feed the arithmetic being compared. */
  {
    char keys[16];
    int letter = -1;
    int slot = 0;
    int j;

    for (j = 0; j < MAX_CLASS; j++)
      if (race[0].rtclass & (0x1L << j))
        {
          if (j == 0)
            letter = slot;
          slot++;
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
  }

  /* The rolling leaves its own labels on the screen; what is compared is what
     the fight draws. */
  clear_screen();
  msg_flag = FALSE;

  py.flags.food = 7500;
  py.flags.food_digested = 2;

  generate_cave();
  strip_traps();

  cave[char_row][char_col].cptr = 1;
  player_light = TRUE;

  /* A weapon worth swinging: a long sword that slays dragons, so tot_dam has
     something to multiply and the monster memory has something to learn. */
  {
    int slot = INVEN_WIELD;

    invcopy(&inventory[slot], 34);
    inventory[slot].flags |= TR_SLAY_DRAGON;
    inventory[slot].tohit = 3;
    inventory[slot].todam = 2;
    known2(&inventory[slot]);
    equip_ctr++;
    inven_weight += inventory[slot].weight;
    py_bonuses(&inventory[slot], 1);
    calc_bonuses();
  }

  /* Spaces to answer the -more- prompts a long fight puts up. */
  {
    char script[4000];
    int n;

    for (n = 0; n < 3999; n++)
      script[n] = ' ';
    script[3999] = '\0';
    oracle_feed_keys(script);
  }

  panel_row = panel_col = -1;
  check_view();

  for (round = 0; round < rounds; round++)
    {
      int y = char_row;
      int x = char_col;
      int placed = FALSE;

      /* Put one beside the player, in the first open square. */
      for (i = 1; i <= 9 && !placed; i++)
        {
          int ty = y;
          int tx = x;

          if (i == 5)
            continue;
          if (!mmove(i, &ty, &tx))
            continue;
          if (cave[ty][tx].fval > MAX_OPEN_SPACE || cave[ty][tx].cptr != 0)
            continue;

          if (place_monster(ty, tx, creature, FALSE))
            {
              m_list[cave[ty][tx].cptr].ml = TRUE;
              py_attack(ty, tx);
              placed = TRUE;
            }
        }

      printf("round %d placed %d exp %ld lev %d mfptr %d\n", round, placed,
             (long)py.misc.exp, (int)py.misc.lev, (int)(mfptr - MIN_MONIX));
    }

  printf("chp %d mhp %d gold %ld\n", (int)py.misc.chp, (int)py.misc.mhp,
         (long)py.misc.au);
  printf("memory kills %d cmove %lu cdefense %d\n",
         (int)c_recall[creature].r_kills,
         (unsigned long)c_recall[creature].r_cmove,
         (int)c_recall[creature].r_cdefense);
  printf("objects %d\n", (int)(tcptr - MIN_TRIX));

  for (i = MIN_TRIX; i < tcptr; i++)
    {
      objdes(description, &t_list[i], TRUE);
      printf("dropped %d %s\n", i, description);
    }

  oracle_screen_dump("fight");

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------------- traps */

/* Standing on things that bite.

   Every trap in the table is sprung in turn, on a fresh character each time, so
   one that kills does not stop the rest. What is compared is the damage, the
   conditions it left behind, and what it did to the level around it. */
static void dump_traps(unsigned long seed, int level, int first, int count)
{
  int which;

  header("traps", seed);
  printf("level %d\n", level);
  printf("first %d\n", first);
  printf("count %d\n", count);

  for (which = first; which < first + count && which < MAX_TRAP; which++)
    {
      int slot;

      probe_init_t_level();
      probe_init_m_level();

      init_seeds((int32u)seed);
      magic_init();
      dun_level = (int16)level;
      pin_player(level);

      init_curses();
      oracle_screen_reset();

      /* The screen is wiped between traps but msg_flag is a global, so without
         this the next trap's message would be run onto a line that has already
         been cleared. Each trap starts with a clean message line. */
      msg_flag = FALSE;

      generate_cave();
      strip_traps();

      cave[char_row][char_col].cptr = 1;
      player_light = TRUE;

      py.misc.chp = 200;
      py.misc.mhp = 200;
      py.flags.food = 7500;

      {
        char script[2001];
        int n;

        for (n = 0; n < 2000; n++)
          script[n] = ' ';
        script[2000] = '\0';
        oracle_feed_keys(script);
      }

      panel_row = panel_col = -1;
      check_view();

      /* The trap goes under the player, which is where a sprung one always
         is. */
      slot = popt();
      invcopy(&t_list[slot], OBJ_TRAP_LIST + which);
      cave[char_row][char_col].tptr = (int8u)slot;

      probe_hit_trap((int)char_row, (int)char_col);

      printf("trap %d chp %d dun %d newlevel %d teleport %d\n", which,
             (int)py.misc.chp, (int)dun_level, new_level_flag ? 1 : 0,
             teleport_flag ? 1 : 0);
      printf("  blind %d confused %d poisoned %d paralysis %d slow %d\n",
             (int)py.flags.blind, (int)py.flags.confused,
             (int)py.flags.poisoned, (int)py.flags.paralysis,
             (int)py.flags.slow);
      printf("  str %d con %d objects %d monsters %d\n",
             (int)py.stats.cur_stat[A_STR], (int)py.stats.cur_stat[A_CON],
             (int)(tcptr - MIN_TRIX), (int)(mfptr - MIN_MONIX));
      printf("  message %s\n", oracle_screen_row(0));

      new_level_flag = FALSE;
      teleport_flag = FALSE;
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ---------------------------------------------------------------- monsters */

/* The monsters taking their turns.

   A level is generated and left as it was found - monsters, objects and all -
   and then creatures() is called over and over with the player standing still.
   Everything the monsters do is compared: where they move, what they open, what
   they eat, what they breed, what they steal, and what the player learns about
   them along the way.

   The level is left exactly as generated, spellcasters included: they breathe,
   summon, blind, drain and teleport, and all of it is compared. */
static void dump_monsters(unsigned long seed, int level, int turns, int variation)
{
  int turn_index, i;

  header("monsters", seed);
  printf("level %d\n", level);
  printf("turns %d\n", turns);
  printf("variation %d\n", variation);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();
  pin_player(level);
  dun_level = (int16)level;

  init_curses();
  oracle_screen_reset();
  msg_flag = FALSE;

  generate_cave();

  cave[char_row][char_col].cptr = 1;
  player_light = TRUE;

  py.misc.chp = 2000;
  py.misc.mhp = 2000;
  py.flags.food = 7500;
  py.misc.stl = 3;

  switch (variation)
    {
    case 0:
      break;
    case 1:
      /* Asleep to begin with, so the waking rolls are exercised. */
      for (i = MIN_MONIX; i < mfptr; i++)
        m_list[i].csleep = 200;
      break;
    case 2:
      /* Aggravated: everything wakes at once and hurries. */
      py.flags.aggravate = TRUE;
      break;
    case 3:
      /* Resting, which changes how often a sleeper checks and how many moves a
         fast monster gets. */
      py.flags.rest = 30000;
      py.flags.status |= PY_REST;
      break;
    default:
      /* Ring the player with things that cast, awake and in range, so the
         spells themselves are compared rather than waited for: breaths,
         summonings, blindness, drained mana and the rest. */
      {
        int placed = 0;
        int kind;

        for (kind = 0; kind < MAX_CREATURES && placed < 8; kind++)
          {
            int dir;

            if ((c_list[kind].spells & CS_FREQ) == 0)
              continue;
            if (c_list[kind].level > dun_level + 10)
              continue;

            for (dir = 1; dir <= 9; dir++)
              {
                int ty = char_row;
                int tx = char_col;

                if (dir == 5)
                  continue;
                if (!mmove(dir, &ty, &tx))
                  continue;
                if (cave[ty][tx].fval > MAX_OPEN_SPACE || cave[ty][tx].cptr != 0)
                  continue;

                if (place_monster(ty, tx, kind, FALSE))
                  {
                    m_list[cave[ty][tx].cptr].csleep = 0;
                    m_list[cave[ty][tx].cptr].ml = TRUE;
                    placed++;
                  }

                break;
              }
          }

        printf("casters %d\n", placed);
      }
      break;
    }

  {
    char script[4000];
    int n;

    for (n = 0; n < 3999; n++)
      script[n] = ' ';
    script[3999] = '\0';
    oracle_feed_keys(script);
  }

  panel_row = panel_col = -1;
  check_view();

  for (turn_index = 0; turn_index < turns; turn_index++)
    {
      turn++;
      creatures(TRUE);
    }

  printf("monsters %d bred %d\n", (int)(mfptr - MIN_MONIX),
         (int)mon_tot_mult);
  printf("chp %d gold %ld packed %d\n", (int)py.misc.chp,
         (long)py.misc.au, (int)inven_ctr);
  printf("blind %d confused %d afraid %d poisoned %d paralysis %d\n",
         (int)py.flags.blind, (int)py.flags.confused, (int)py.flags.afraid,
         (int)py.flags.poisoned, (int)py.flags.paralysis);

  for (i = MIN_MONIX; i < mfptr; i++)
    printf("mon %d at %d %d kind %d hp %d sleep %d stun %d conf %d ml %d\n",
           i, (int)m_list[i].fy, (int)m_list[i].fx, (int)m_list[i].mptr,
           (int)m_list[i].hp, (int)m_list[i].csleep, (int)m_list[i].stunned,
           (int)m_list[i].confused, m_list[i].ml ? 1 : 0);

  /* What the fight taught the player about each kind that took part. */
  for (i = 0; i < MAX_CREATURES; i++)
    if (c_recall[i].r_cmove || c_recall[i].r_spells || c_recall[i].r_wake
        || c_recall[i].r_ignore || c_recall[i].r_attacks[0] || c_recall[i].r_kills)
      printf("recall %d move %lu spells %lu wake %d ignore %d attacks %d %d %d %d\n",
             i, (unsigned long)c_recall[i].r_cmove,
             (unsigned long)c_recall[i].r_spells, (int)c_recall[i].r_wake,
             (int)c_recall[i].r_ignore, (int)c_recall[i].r_attacks[0],
             (int)c_recall[i].r_attacks[1], (int)c_recall[i].r_attacks[2],
             (int)c_recall[i].r_attacks[3]);

  printf("objects %d\n", (int)(tcptr - MIN_TRIX));

  oracle_screen_dump("mon");

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------------ potion */

/* Drinking things.

   Every potion in the table is drunk by a fresh character, and what it did is
   compared: the stats, the counters, the experience, the messages and whether
   the player worked out what it was.

   quaff() itself asks which potion to drink, through the inventory screen that
   is not ported. The potion is put in the pack and the letter is fed to it, so
   the real quaff() runs - prompting, effects, food and all. */
static void dump_potion(unsigned long seed, int first, int count)
{
  int which;

  header("potion", seed);
  printf("first %d\n", first);
  printf("count %d\n", count);

  probe_init_t_level();
  probe_init_m_level();

  /* magic_init() shuffles the appearance tables where they stand, so calling it
     once per potion would shuffle an already-shuffled table. It runs once, and
     only the generator is re-seeded for each potion. */
  init_seeds((int32u)seed);
  magic_init();

  for (which = first; which < first + count && which < MAX_OBJECTS; which++)
    {
      int tval;
      inven_type sample_potion;

      init_seeds((int32u)seed);
      pin_player(0);
      dun_level = 1;

      init_curses();
      oracle_screen_reset();
      msg_flag = FALSE;

      tval = object_list[which].tval;
      if (tval != TV_POTION1 && tval != TV_POTION2 && tval != TV_FOOD)
        continue;

      /* A character with room to improve in every direction: hurt, drained,
         hungry and poisoned, so that a cure has something to cure. */
      py.misc.lev = 10;
      py.misc.expfact = 100;
      py.misc.exp = 2000;
      py.misc.max_exp = 5000;
      py.misc.mhp = 80;
      py.misc.chp = 30;
      py.misc.mana = 20;
      py.misc.cmana = 5;
      py.flags.food = 3000;
      py.flags.poisoned = 20;
      py.flags.confused = 20;
      py.flags.blind = 20;
      py.flags.afraid = 20;

      {
        int i;

        for (i = 0; i < 6; i++)
          {
            py.stats.max_stat[i] = 16;
            py.stats.cur_stat[i] = 12;
            py.stats.mod_stat[i] = 0;
            set_use_stat(i);
          }
      }

      /* Everything the player knows is forgotten between potions: each one is
         drunk by someone who has never seen one, which is what makes the
         identification worth comparing. */
      (void) memset((char *)object_ident, 0, OBJECT_IDENT_SIZE);

      inven_ctr = 1;
      invcopy(&inventory[0], which);
      inven_weight = inventory[0].weight;

      {
        char script[200];
        int n;

        script[0] = 'a';
        for (n = 1; n < 199; n++)
          script[n] = ' ';
        script[199] = '\0';
        oracle_feed_keys(script);
      }

      if (tval == TV_FOOD)
        eat();
      else
        quaff();

      printf("potion %d tval %d flags %lu\n", which, tval,
             (unsigned long)object_list[which].flags);
      printf("  chp %d mana %d exp %ld lev %d food %d\n", (int)py.misc.chp,
             (int)py.misc.cmana, (long)py.misc.exp, (int)py.misc.lev,
             (int)py.flags.food);
      printf("  stats %d %d %d %d %d %d\n", (int)py.stats.cur_stat[0],
             (int)py.stats.cur_stat[1], (int)py.stats.cur_stat[2],
             (int)py.stats.cur_stat[3], (int)py.stats.cur_stat[4],
             (int)py.stats.cur_stat[5]);
      printf("  blind %d conf %d afraid %d pois %d para %d\n",
             (int)py.flags.blind, (int)py.flags.confused, (int)py.flags.afraid,
             (int)py.flags.poisoned, (int)py.flags.paralysis);
      printf("  fast %d slow %d hero %d shero %d invuln %d\n",
             (int)py.flags.fast, (int)py.flags.slow, (int)py.flags.hero,
             (int)py.flags.shero, (int)py.flags.invuln);
      printf("  heat %d cold %d detinv %d infra %d prot %d\n",
             (int)py.flags.resist_heat, (int)py.flags.resist_cold,
             (int)py.flags.detect_inv, (int)py.flags.tim_infra,
             (int)py.flags.protevil);
      /* Asked about a fresh copy rather than the slot, which the potion has
         just been destroyed out of. */
      invcopy(&sample_potion, which);
      printf("  known %d packed %d message %s\n",
             known1_p(&sample_potion) ? 1 : 0, (int)inven_ctr,
             oracle_screen_row(0));
      printf("  state %lu\n", (unsigned long)get_rnd_seed());
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------------ device */

/* Reading scrolls, aiming wands, using staffs.

   Each item in the table is used once by the same character on the same freshly
   generated level, and everything it did is compared: the player, the level, the
   monsters left standing, the messages, and the generator.

   These reach further than a potion does - a scroll can wall the player in, a
   wand can dissolve a corridor - so the level is dumped as a set of counts
   rather than square by square, with the object and monster totals beside it.

   The real read_scroll(), aim() and use() run, prompting and all. The prompts
   are answered by a scripted key: "a" picks the first pack slot, "6" points
   east, and "k" is the letter fed to a scroll of genocide. Which of those a
   given item needs is known from its flags, so the script is built to match. */

#define SCROLL_IDENTIFY_BIT  0x00000008L
#define SCROLL_RECHARGE_BIT  0x01000000L
#define SCROLL_GENOCIDE_BIT  0x02000000L

static void device_script(int tval, int32u flags)
{
  char script[600];
  int n = 0;
  int i;

  /* The item itself. */
  script[n++] = (char)'a';

  if (tval == TV_WAND)
    script[n++] = (char)'6';

  /* These three announce themselves before they ask, and the message waiting on
     the message line turns the prompt that follows into a -more- first. The
     escape dismisses that, so the answer lands on the prompt itself. */
  if (tval == TV_SCROLL1)
    {
      if (flags & SCROLL_IDENTIFY_BIT)
        {
          script[n++] = (char)27;
          script[n++] = (char)'a';
        }
      if (flags & SCROLL_RECHARGE_BIT)
        {
          script[n++] = (char)27;
          script[n++] = (char)'a';
        }
      if (flags & SCROLL_GENOCIDE_BIT)
        {
          script[n++] = (char)27;
          script[n++] = (char)'k';
        }
    }

  /* Padded with escapes rather than spaces: a space is not an answer to any of
     these prompts, so a prompt that asked for one more key than the script
     provides would spin on the padding instead of giving up. */
  for (i = n; i < 599; i++)
    script[i] = (char)27;
  script[599] = 0;

  oracle_feed_keys(script);
}

static void dump_device(const char *mode, unsigned long seed, int level,
                        int first, int count)
{
  int which;
  int wanted1, wanted2;

  if (strcmp(mode, "scroll") == 0)
    {
      wanted1 = TV_SCROLL1;
      wanted2 = TV_SCROLL2;
    }
  else if (strcmp(mode, "wand") == 0)
    {
      wanted1 = TV_WAND;
      wanted2 = TV_WAND;
    }
  else
    {
      wanted1 = TV_STAFF;
      wanted2 = TV_STAFF;
    }

  header(mode, seed);
  printf("level %d\n", level);
  printf("first %d\n", first);
  printf("count %d\n", count);

  probe_init_t_level();
  probe_init_m_level();

  /* magic_init() shuffles the appearance tables where they stand, so it runs
     once and only the generator is re-seeded for each item. */
  init_seeds((int32u)seed);
  magic_init();

  for (which = first; which < first + count && which < MAX_OBJECTS; which++)
    {
      int tval;
      int i;
      int lit, marked, walls;
      inven_type sample;

      tval = object_list[which].tval;
      if (tval != wanted1 && tval != wanted2)
        continue;

      init_seeds((int32u)seed);
      pin_player(level);
      dun_level = (int16)level;

      init_curses();
      oracle_screen_reset();
      msg_flag = FALSE;

      generate_cave();
      cave[char_row][char_col].cptr = 1;

      /* Someone who can work a device and survive what it wakes. */
      py.misc.lev = 20;
      py.misc.expfact = 100;
      /* No experience to start with: the level is pinned at twenty, and any
         experience worth a level would be spent gaining it before the scroll
         was read. */
      py.misc.exp = 0;
      py.misc.max_exp = 0;
      py.misc.hitdie = 10;
      py.misc.save = 40;
      py.flags.food = 5000;

      for (i = 0; i < 6; i++)
        {
          py.stats.max_stat[i] = 18;
          py.stats.cur_stat[i] = 18;
          py.stats.mod_stat[i] = 0;
          set_use_stat(i);
        }

      /* Everything the player knows is forgotten between items. */
      (void) memset((char *)object_ident, 0, OBJECT_IDENT_SIZE);

      /* A weapon, a suit of armour and a light, so the enchanting and cursing
         scrolls have something to work on and a scroll can be read at all. */
      inven_ctr = 0;
      inven_weight = 0;
      equip_ctr = 0;

      invcopy(&inventory[INVEN_WIELD], 30);   /* a stiletto */
      invcopy(&inventory[INVEN_BODY], 103);   /* soft leather armor */
      invcopy(&inventory[INVEN_HEAD], 96);    /* a hard leather cap */
      invcopy(&inventory[INVEN_LIGHT], 365);  /* a wooden torch */
      inventory[INVEN_LIGHT].p1 = 5000;
      equip_ctr = 4;

      /* calc_bonuses() recomputes the hit points from the class and the
         constitution, so the survivable totals are set after it rather than
         before. */
      calc_bonuses();

      py.misc.mhp = 500;
      py.misc.chp = 500;
      py.misc.mana = 50;
      py.misc.cmana = 50;

      player_light = TRUE;
      panel_row = panel_col = -1;
      check_view();

      invcopy(&inventory[0], which);
      /* Charges, so a wand or a staff has something to spend. */
      if (tval == TV_WAND || tval == TV_STAFF)
        inventory[0].p1 = 15;
      inven_ctr = 1;
      inven_weight = inventory[0].weight;

      /* Cleared here rather than at the top: generating the level and lighting
         it can leave a message waiting, and a waiting message turns the first
         message the scroll prints into a -more- prompt that eats a scripted
         key. */
      msg_flag = FALSE;

      device_script(tval, object_list[which].flags);

      free_turn_flag = FALSE;
      new_level_flag = FALSE;

      if (tval == TV_WAND)
        aim();
      else if (tval == TV_STAFF)
        use();
      else
        read_scroll();

      lit = 0;
      marked = 0;
      walls = 0;
      for (i = 0; i < cur_height; i++)
        {
          int j;

          for (j = 0; j < cur_width; j++)
            {
              if (cave[i][j].pl || cave[i][j].tl)
                lit++;
              if (cave[i][j].fm)
                marked++;
              if (cave[i][j].fval >= MIN_CAVE_WALL)
                walls++;
            }
        }

      printf("item %d tval %d flags %lu\n", which, tval,
             (unsigned long)object_list[which].flags);
      printf("  chp %d mana %d exp %ld food %d dlev %d newlev %d free %d\n",
             (int)py.misc.chp, (int)py.misc.cmana, (long)py.misc.exp,
             (int)py.flags.food, (int)dun_level, new_level_flag ? 1 : 0,
             free_turn_flag ? 1 : 0);
      printf("  at %d %d blind %d conf %d afraid %d prot %d recall %d\n",
             (int)char_row, (int)char_col, (int)py.flags.blind,
             (int)py.flags.confused, (int)py.flags.afraid,
             (int)py.flags.protevil, (int)py.flags.word_recall);
      printf("  fast %d slow %d blessed %d confmon %d\n",
             (int)py.flags.fast, (int)py.flags.slow, (int)py.flags.blessed,
             py.flags.confuse_monster ? 1 : 0);
      printf("  monsters %d objects %d lit %d marked %d walls %d\n",
             (int)(mfptr - MIN_MONIX), (int)(tcptr - MIN_TRIX), lit, marked,
             walls);
      printf("  wield %d %d %d body %d %d head %d %d\n",
             (int)inventory[INVEN_WIELD].tohit, (int)inventory[INVEN_WIELD].todam,
             (int)inventory[INVEN_WIELD].toac, (int)inventory[INVEN_BODY].toac,
             (int)inventory[INVEN_BODY].flags ? 1 : 0,
             (int)inventory[INVEN_HEAD].toac,
             (int)inventory[INVEN_HEAD].flags ? 1 : 0);

      invcopy(&sample, which);
      printf("  known %d packed %d charges %d message %s\n",
             known1_p(&sample) ? 1 : 0, (int)inven_ctr,
             (int)inventory[0].p1, oracle_screen_row(0));
      printf("  state %lu\n", (unsigned long)get_rnd_seed());
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------------- magic */

/* Casting a mage's spells and reciting a priest's prayers.

   Every one of the thirty-one spells a class has is cast twice by the same
   character on the same freshly generated level: once with mana to spare, and
   once with almost none, since running short changes both how likely the spell
   is to fail and what it costs to cast it anyway.

   The real cast() and pray() run, prompting and all. The prompts are answered
   by a scripted key: "a" picks the book, the spell's own letter picks the
   spell, "y" confirms a spell too expensive to afford, "6" points east, and "k"
   is the letter fed to genocide. Which of those a given spell needs is known
   from its number, so the script is built to match. */

/* Whether a mage spell asks which way it goes. */
static int mage_spell_aims(int spell)
{
  switch (spell + 1)
    {
    case 1: case 7: case 8: case 9: case 11: case 15: case 16:
    case 20: case 23: case 24: case 25: case 27: case 29:
      return TRUE;
    default:
      return FALSE;
    }
}

/* Whether a prayer asks which way it goes. */
static int prayer_aims(int spell)
{
  return (spell + 1) == 9 || (spell + 1) == 18;
}

static void magic_script(int mage, int spell, int letter, int confirm)
{
  char script[600];
  int n = 0;
  int i;

  /* The book, then the spell within it. */
  script[n++] = (char)'a';
  script[n++] = (char)letter;

  if (confirm)
    script[n++] = (char)'y';

  if (mage ? mage_spell_aims(spell) : prayer_aims(spell))
    script[n++] = (char)'6';

  if (mage)
    {
      /* Recharge I, Recharge II and Identify all ask which item. */
      if (spell + 1 == 18 || spell + 1 == 21 || spell + 1 == 26)
        script[n++] = (char)'a';

      /* Genocide asks for a letter. */
      if (spell + 1 == 31)
        script[n++] = (char)'k';
    }

  /* Padded with escapes rather than spaces: a space is not an answer to any of
     these prompts, so a prompt that asked for one more key than the script
     provides would spin on the padding instead of giving up. */
  for (i = n; i < 599; i++)
    script[i] = (char)27;
  script[599] = 0;

  oracle_feed_keys(script);
}

static void dump_magic(const char *mode, unsigned long seed, int level,
                       int first, int count)
{
  int mage;
  int book_tval;
  int spell;

  mage = strcmp(mode, "spell") == 0;
  book_tval = mage ? TV_MAGIC_BOOK : TV_PRAYER_BOOK;

  header(mode, seed);
  printf("level %d\n", level);
  printf("first %d\n", first);
  printf("count %d\n", count);

  probe_init_t_level();
  probe_init_m_level();

  /* magic_init() shuffles the appearance tables where they stand, so it runs
     once and only the generator is re-seeded for each cast. */
  init_seeds((int32u)seed);
  magic_init();

  for (spell = first; spell < first + count && spell < 31; spell++)
    {
      int book;
      int which;
      int first_spell;
      int32u holder;
      int letter;
      int pass;

      /* Which book the spell is printed in, and which letter it has there. The
         lettering runs from the first spell in the book whether or not the
         character knows it, so a spell keeps its letter as they learn. */
      book = -1;
      for (which = 0; which < MAX_OBJECTS; which++)
        if (object_list[which].tval == book_tval
            && (object_list[which].flags & (1L << spell)))
          {
            book = which;
            break;
          }

      if (book < 0)
        continue;

      holder = object_list[book].flags;
      first_spell = bit_pos(&holder);
      letter = 'a' + spell - first_spell;

      for (pass = 0; pass < 2; pass++)
        {
          int i;
          int lit, marked, walls;
          int mana, confirm;

          /* Plenty of mana, then almost none. */
          mana = (pass == 0) ? 200 : 1;
          confirm = magic_spell[mage ? 0 : 1][spell].smana > mana;

          init_seeds((int32u)seed);
          pin_player(level);
          py.misc.pclass = mage ? 1 : 2;   /* class 0 is the warrior */
          dun_level = (int16)level;

          /* These live outside the player struct, so pin_player() does not
             clear them and one cast would otherwise be set up by the last. */
          spell_learned = 0;
          spell_worked = 0;
          spell_forgotten = 0;
          for (i = 0; i < 32; i++)
            spell_order[i] = 99;

          init_curses();
          oracle_screen_reset();
          msg_flag = FALSE;

          generate_cave();
          cave[char_row][char_col].cptr = 1;

          /* Someone who can cast anything and survive what it wakes. No
             experience to start with, since the level is pinned. */
          py.misc.lev = 40;
          py.misc.expfact = 100;
          py.misc.exp = 0;
          py.misc.max_exp = 0;
          py.misc.hitdie = 10;
          py.misc.save = 40;
          py.flags.food = 5000;

          for (i = 0; i < 6; i++)
            {
              py.stats.max_stat[i] = 18;
              py.stats.cur_stat[i] = 18;
              py.stats.mod_stat[i] = 0;
              set_use_stat(i);
            }

          /* Everything the player knows is forgotten between casts. */
          (void) memset((char *)object_ident, 0, OBJECT_IDENT_SIZE);

          /* A weapon, a suit of armour and a light: without a light nothing can
             be read at all, spell book included. */
          inven_ctr = 0;
          inven_weight = 0;
          equip_ctr = 0;

          invcopy(&inventory[INVEN_WIELD], 30);   /* a stiletto */
          invcopy(&inventory[INVEN_BODY], 103);   /* soft leather armor */
          invcopy(&inventory[INVEN_HEAD], 96);    /* a hard leather cap */
          invcopy(&inventory[INVEN_LIGHT], 365);  /* a wooden torch */
          inventory[INVEN_LIGHT].p1 = 5000;
          equip_ctr = 4;

          /* calc_bonuses() recomputes the hit points from the class and the
             constitution, so the survivable totals are set after it. */
          calc_bonuses();

          py.misc.mhp = 500;
          py.misc.chp = 500;
          py.misc.mana = 200;
          py.misc.cmana = mana;
          py.misc.cmana_frac = 0;

          player_light = TRUE;
          panel_row = panel_col = -1;
          check_view();

          invcopy(&inventory[0], book);
          inven_ctr = 1;
          inven_weight = inventory[0].weight;

          /* Set after set_use_stat(), which would otherwise forget spells the
             character is not yet entitled to. */
          spell_learned = 0x7FFFFFFFL;
          spell_worked = 0;
          spell_forgotten = 0;
          for (i = 0; i < 32; i++)
            spell_order[i] = 99;

          /* Cleared here rather than at the top: generating the level and
             lighting it can leave a message waiting, and a waiting message
             turns the first prompt into a -more- that eats a scripted key. */
          msg_flag = FALSE;

          magic_script(mage, spell, letter, confirm);

          free_turn_flag = FALSE;
          new_level_flag = FALSE;

          if (mage)
            cast();
          else
            pray();

          lit = 0;
          marked = 0;
          walls = 0;
          for (i = 0; i < cur_height; i++)
            {
              int j;

              for (j = 0; j < cur_width; j++)
                {
                  if (cave[i][j].pl || cave[i][j].tl)
                    lit++;
                  if (cave[i][j].fm)
                    marked++;
                  if (cave[i][j].fval >= MIN_CAVE_WALL)
                    walls++;
                }
            }

          printf("spell %d pass %d book %d letter %c\n", spell, pass, book,
                 (char)letter);
          printf("  chp %d mana %d frac %d exp %ld free %d newlev %d\n",
                 (int)py.misc.chp, (int)py.misc.cmana,
                 (int)py.misc.cmana_frac, (long)py.misc.exp,
                 free_turn_flag ? 1 : 0, new_level_flag ? 1 : 0);
          printf("  at %d %d para %d conf %d afraid %d prot %d invuln %d\n",
                 (int)char_row, (int)char_col, (int)py.flags.paralysis,
                 (int)py.flags.confused, (int)py.flags.afraid,
                 (int)py.flags.protevil, (int)py.flags.invuln);
          printf("  fast %d blessed %d heat %d cold %d detinv %d\n",
                 (int)py.flags.fast, (int)py.flags.blessed,
                 (int)py.flags.resist_heat, (int)py.flags.resist_cold,
                 (int)py.flags.detect_inv);
          printf("  worked %lu learned %lu forgot %lu newspells %d\n",
                 (unsigned long)spell_worked, (unsigned long)spell_learned,
                 (unsigned long)spell_forgotten, (int)py.flags.new_spells);
          printf("  monsters %d objects %d lit %d marked %d walls %d\n",
                 (int)(mfptr - MIN_MONIX), (int)(tcptr - MIN_TRIX), lit, marked,
                 walls);
          printf("  con %d food %d packed %d message %s\n",
                 (int)py.stats.cur_stat[A_CON], (int)py.flags.food,
                 (int)inven_ctr, oracle_screen_row(0));
          printf("  state %lu\n", (unsigned long)get_rnd_seed());
        }
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------------- inven */

/* The inventory screens, and the prompt that asks which item.

   The real show_inven(), show_equip(), inven_command() and get_item() run, and
   the whole screen is compared afterwards along with the pack, the equipment
   and the weight. The layout is the point here: both lists are drawn as far
   right as the longest line allows, so a description one character longer moves
   the entire column.

   The character is given the same pack every time - one item of each of eight
   kinds, picked as the first of its kind in the object table - so the letters
   and the widths are fixed and only the keys change. */

static int inven_kinds[] = {
  TV_RING, TV_AMULET, TV_SHIELD, TV_HELM, TV_BOOTS, TV_CLOAK,
  TV_POTION1, TV_FOOD, TV_SCROLL1, TV_WAND
};

/* The scripts. The first character is the command inven_command() is called
   with; the rest are fed to it as keys. */
static char *inven_scripts[] = {
  "i\033",              /* list the pack                                  */
  "e\033",              /* list what is worn                              */
  "?\033",              /* the help screen                                */
  "iew\033",            /* both lists, then back out of wearing           */
  "x\033",              /* swap the wielded weapon with the spare         */
  "wa\033",             /* wear the first thing that can be worn          */
  "wA\033",             /* the same, but confirmed first                  */
  "wAy\033",            /* the same, confirmed                            */
  "ta\033",             /* take the first worn thing off                  */
  "da\033",             /* drop the first thing in the pack               */
  "day\033",            /* drop it, all of it                             */
  "e d a\033",          /* from the equipment list, throw something off   */
  "d/a\033",            /* drop, swapped over to the equipment list       */
  "izz\033",            /* two keys that are not commands at all          */
  "wz\033",             /* a letter outside the range on offer            */
  "w*a\033",            /* list what could be worn, then wear it          */
  "iwawa\033",          /* the list up, then two things worn in a row     */
  "iwawa\033",          /* the same, with two of the same ring carried    */
  "itata\033",          /* two things taken off in a row                  */
  "idaydayd\033"        /* dropped until there is no room left            */
};

#define INVEN_SCRIPTS 20

static void dump_inven(unsigned long seed, int variation)
{
  int i, k;
  char *script;
  int rounds;

  header("inven", seed);
  printf("variation %d\n", variation);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();

  init_seeds((int32u)seed);
  pin_player(5);
  dun_level = 5;

  init_curses();
  oracle_screen_reset();
  msg_flag = FALSE;

  generate_cave();
  cave[char_row][char_col].cptr = 1;

  py.misc.lev = 20;
  py.misc.expfact = 100;
  py.misc.hitdie = 10;
  py.flags.food = 5000;

  for (i = 0; i < 6; i++)
    {
      py.stats.max_stat[i] = 18;
      py.stats.cur_stat[i] = 18;
      py.stats.mod_stat[i] = 0;
      set_use_stat(i);
    }

  (void) memset((char *)object_ident, 0, OBJECT_IDENT_SIZE);

  inven_ctr = 0;
  inven_weight = 0;
  equip_ctr = 0;

  invcopy(&inventory[INVEN_WIELD], 30);   /* a stiletto           */
  invcopy(&inventory[INVEN_AUX], 34);     /* a spare weapon       */
  invcopy(&inventory[INVEN_BODY], 103);   /* soft leather armor   */
  invcopy(&inventory[INVEN_LIGHT], 365);  /* a wooden torch       */
  inventory[INVEN_LIGHT].p1 = 5000;
  equip_ctr = 4;

  calc_bonuses();

  py.misc.mhp = 500;
  py.misc.chp = 500;

  player_light = TRUE;
  panel_row = panel_col = -1;
  check_view();

  /* One of each kind, the first of its kind in the table, so the pack is the
     same every time and the letters do not move. */
  for (k = 0; k < (int)(sizeof(inven_kinds)/sizeof(inven_kinds[0])); k++)
    {
      inven_type held;

      for (i = 0; i < MAX_OBJECTS; i++)
        if (object_list[i].tval == inven_kinds[k])
          {
            invcopy(&held, i);
            (void) inven_carry(&held);
            break;
          }
    }

  /* A second ring for one variation, so that wearing has to take one out of a
     pile rather than the whole of it. */
  if (variation == 17)
    for (i = 0; i < MAX_OBJECTS; i++)
      if (object_list[i].tval == TV_RING)
        {
          inven_type held;

          invcopy(&held, i);
          (void) inven_carry(&held);
          break;
        }

  /* Something on the floor for one variation, so that dropping has to say
     there is no room. */
  if (variation == 12)
    {
      i = popt();
      invcopy(&t_list[i], 30);
      cave[char_row][char_col].tptr = i;
    }

  msg_flag = FALSE;
  free_turn_flag = FALSE;
  doing_inven = 0;

  /* Every other variation shows the weights, which narrows the room left for
     the descriptions and so moves the whole column. */
  show_weight_flag = (variation % 2);

  script = inven_scripts[variation % INVEN_SCRIPTS];
  {
    char keys[600];
    int n = 0;

    for (i = 1; script[i]; i++)
      keys[n++] = script[i];
    for (i = n; i < 599; i++)
      keys[i] = (char)27;
    keys[599] = 0;
    oracle_feed_keys(keys);
  }

  /* Called again while it says it is not finished, as the main loop does -
     the mode gives the world a turn and comes back. */
  rounds = 0;
  do
    {
      char command = rounds == 0 ? script[0] : (char)doing_inven;

      free_turn_flag = FALSE;
      inven_command(command);

      printf("round %d free %d doing %d\n", rounds, free_turn_flag ? 1 : 0,
             (int)doing_inven);
      printf("  packed %d equipped %d weight %d\n", (int)inven_ctr,
             (int)equip_ctr, (int)inven_weight);

      for (i = 0; i < inven_ctr; i++)
        {
          bigvtype name;

          objdes(name, &inventory[i], TRUE);
          printf("  pack %d %d %s\n", i, (int)inventory[i].number, name);
        }

      for (i = INVEN_WIELD; i < INVEN_ARRAY_SIZE; i++)
        if (inventory[i].tval != TV_NOTHING)
          {
            bigvtype name;

            objdes(name, &inventory[i], TRUE);
            printf("  worn %d %s\n", i, name);
          }

      printf("  floor %d\n", (int)cave[char_row][char_col].tptr);
      rounds++;
    }
  while (doing_inven && rounds < 4);

  oracle_screen_dump("scr");

  /* The two lists on their own. inven_command() puts the screen back when it
     leaves, so the layout has to be drawn again to be compared - and the
     layout is the whole point: one character more in one description moves the
     entire column. */
  clear_screen();
  printf("inven-col %d\n", show_inven(0, inven_ctr - 1, show_weight_flag, 50, CNIL));
  oracle_screen_dump("list");

  clear_screen();
  printf("equip-col %d\n", show_equip(show_weight_flag, 50));
  oracle_screen_dump("worn");

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* get_item() on its own, which is what every command that asks which item goes
   through. The range and the keys change; the answer and the screen are
   compared. */
static char *getitem_scripts[] = {
  "a",          /* the first slot                                        */
  "c",          /* the third                                             */
  "\033",       /* backed out of                                         */
  "*a",         /* listed first, then chosen                             */
  "z",          /* outside the range                                     */
  "A",          /* a capital, which asks first                           */
  "Ay",         /* a capital, confirmed                                  */
  "/a",         /* swapped to the equipment list                         */
  "*/a",        /* listed, then swapped, then chosen                     */
  "2"           /* picked by its inscription rather than its letter      */
};

#define GETITEM_SCRIPTS 10

static void dump_getitem(unsigned long seed, int variation)
{
  int i, k, chosen, taken;
  char *script;

  header("getitem", seed);
  printf("variation %d\n", variation);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();

  init_seeds((int32u)seed);
  pin_player(5);
  dun_level = 5;

  init_curses();
  oracle_screen_reset();
  msg_flag = FALSE;

  generate_cave();
  cave[char_row][char_col].cptr = 1;

  py.misc.lev = 20;
  py.misc.expfact = 100;
  py.misc.hitdie = 10;
  py.flags.food = 5000;

  for (i = 0; i < 6; i++)
    {
      py.stats.max_stat[i] = 18;
      py.stats.cur_stat[i] = 18;
      py.stats.mod_stat[i] = 0;
      set_use_stat(i);
    }

  (void) memset((char *)object_ident, 0, OBJECT_IDENT_SIZE);

  inven_ctr = 0;
  inven_weight = 0;
  equip_ctr = 0;

  invcopy(&inventory[INVEN_WIELD], 30);
  invcopy(&inventory[INVEN_BODY], 103);
  invcopy(&inventory[INVEN_LIGHT], 365);
  inventory[INVEN_LIGHT].p1 = 5000;
  equip_ctr = 3;

  calc_bonuses();

  py.misc.mhp = 500;
  py.misc.chp = 500;

  player_light = TRUE;
  panel_row = panel_col = -1;
  check_view();

  for (k = 0; k < (int)(sizeof(inven_kinds)/sizeof(inven_kinds[0])); k++)
    for (i = 0; i < MAX_OBJECTS; i++)
      if (object_list[i].tval == inven_kinds[k])
        {
          inven_type held;

          invcopy(&held, i);
          (void) inven_carry(&held);
          break;
        }

  /* An inscription, so that a digit has something to find. */
  (void) strcpy(inventory[1].inscrip, "2");

  msg_flag = FALSE;
  free_turn_flag = FALSE;

  script = getitem_scripts[variation % GETITEM_SCRIPTS];
  {
    char keys[600];
    int n = 0;

    for (i = 0; script[i]; i++)
      keys[n++] = script[i];
    for (i = n; i < 599; i++)
      keys[i] = (char)27;
    keys[599] = 0;
    oracle_feed_keys(keys);
  }

  chosen = -1;
  taken = get_item(&chosen, "Which one?", 0, INVEN_ARRAY_SIZE, CNIL, CNIL);

  /* The slot is only meaningful when something was picked: get_item() writes
     to it as it goes and leaves whatever it last worked out there, which no
     caller looks at. */
  printf("taken %d slot %d free %d\n", taken ? 1 : 0, taken ? chosen : -1,
         free_turn_flag ? 1 : 0);

  oracle_screen_dump("scr");

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------------ moria4 */

/* Digging, disarming, bashing and throwing.

   None of these can be compared on a level as it was generated: whether there
   is rubble next to the player is a matter of luck, and a run that finds none
   would prove nothing. So the eight squares around the player are set to a
   known arrangement first - a wall of each kind, rubble, a secret door, a
   locked door, a chest and a trap - and each variation then aims its action at
   whichever of them it is about.

   Every action is repeated two dozen times, since digging and bashing are both
   a matter of trying until the thing gives. The prompts are answered with a
   scripted key, fed fresh each round. */

#define MORIA4_ROUNDS 24
#define MORIA4_VARIATIONS 10

/* What goes on each of the eight squares around the player. */
static void moria4_surround(void)
{
  int slot;

  /* Three kinds of rock to dig, and rubble. */
  cave[char_row-1][char_col].fval = GRANITE_WALL;
  cave[char_row+1][char_col].fval = MAGMA_WALL;
  cave[char_row][char_col+1].fval = QUARTZ_WALL;

  cave[char_row][char_col-1].fval = CORR_FLOOR;
  slot = popt();
  invcopy(&t_list[slot], OBJ_RUBBLE);
  cave[char_row][char_col-1].tptr = (int8u)slot;

  /* A secret door, which digs like the granite it is hiding in. */
  cave[char_row-1][char_col+1].fval = GRANITE_WALL;
  slot = popt();
  invcopy(&t_list[slot], OBJ_SECRET_DOOR);
  cave[char_row-1][char_col+1].tptr = (int8u)slot;

  /* A door to bash, locked hard enough to take a few tries. */
  cave[char_row-1][char_col-1].fval = CORR_FLOOR;
  slot = popt();
  invcopy(&t_list[slot], OBJ_CLOSED_DOOR);
  t_list[slot].p1 = 8;
  cave[char_row-1][char_col-1].tptr = (int8u)slot;

  /* A chest, locked and trapped, to disarm or to smash. */
  cave[char_row+1][char_col+1].fval = CORR_FLOOR;
  slot = popt();
  invcopy(&t_list[slot], 326);
  t_list[slot].flags = CH_LOCKED | CH_LOSE_STR | CH_POISON;
  t_list[slot].level = 10;
  /* Known, since a chest's trap has to be seen before it can be worked on. */
  known2(&t_list[slot]);
  cave[char_row+1][char_col+1].tptr = (int8u)slot;

  /* And a trap in the floor. */
  cave[char_row+1][char_col-1].fval = CORR_FLOOR;
  slot = popt();
  invcopy(&t_list[slot], OBJ_TRAP_LIST + 1);
  /* Found, as a trap has to be before it can be disarmed. What change_trap()
     does when the player notices one. */
  t_list[slot].tval = TV_VIS_TRAP;
  t_list[slot].tchar = '^';
  cave[char_row+1][char_col-1].tptr = (int8u)slot;

  /* Nothing is standing on any of them. */
  cave[char_row-1][char_col].cptr = 0;
  cave[char_row+1][char_col].cptr = 0;
  cave[char_row][char_col+1].cptr = 0;
  cave[char_row][char_col-1].cptr = 0;
  cave[char_row-1][char_col+1].cptr = 0;
  cave[char_row-1][char_col-1].cptr = 0;
  cave[char_row+1][char_col+1].cptr = 0;
  cave[char_row+1][char_col-1].cptr = 0;
}

/* A lane to the east with something in it, so that a thrown thing has
   somewhere to fly and something to hit. */
static void moria4_target(int adjacent)
{
  int j;

  for (j = 1; j <= 8; j++)
    if (in_bounds(char_row, char_col + j))
      {
        cave[char_row][char_col+j].fval = CORR_FLOOR;
        cave[char_row][char_col+j].tptr = 0;
        cave[char_row][char_col+j].cptr = 0;
      }

  j = adjacent ? 1 : 5;

  if (in_bounds(char_row, char_col + j))
    (void) place_monster(char_row, char_col + j, 20, FALSE);
}

/* What the player is holding, which decides how well they dig and throw. */
static void moria4_wield(int variation)
{
  int i;

  inven_ctr = 0;
  inven_weight = 0;
  equip_ctr = 0;

  invcopy(&inventory[INVEN_BODY], 103);   /* soft leather armor */
  invcopy(&inventory[INVEN_ARM], 111);    /* a shield, which a bash hits with */
  invcopy(&inventory[INVEN_LIGHT], 365);  /* a wooden torch */
  inventory[INVEN_LIGHT].p1 = 5000;
  equip_ctr = 3;

  if (variation == 1)
    {
      /* A shovel, whose digging plus is worth far more than any weapon. */
      for (i = 0; i < MAX_OBJECTS; i++)
        if (object_list[i].tval == TV_DIGGING)
          {
            invcopy(&inventory[INVEN_WIELD], i);
            inventory[INVEN_WIELD].p1 = 2;
            equip_ctr++;
            break;
          }
    }
  else if (variation == 2)
    {
      /* Bare hands, which dig nothing at all. */
    }
  else if (variation == 8)
    {
      /* A bow, so that the arrows are fired rather than thrown. */
      for (i = 0; i < MAX_OBJECTS; i++)
        if (object_list[i].tval == TV_BOW)
          {
            invcopy(&inventory[INVEN_WIELD], i);
            /* A short bow, which is what an arrow is made for. */
            inventory[INVEN_WIELD].p1 = 2;
            equip_ctr++;
            break;
          }
    }
  else
    {
      invcopy(&inventory[INVEN_WIELD], 30);   /* a stiletto */
      equip_ctr++;
    }
}

/* A pack of things to throw: one of each of a few kinds, and a quiver. */
static void moria4_pack(void)
{
  int i, k;
  static int kinds[] = { TV_ARROW, TV_FLASK, TV_POTION1, TV_FOOD, TV_HAFTED };

  for (k = 0; k < (int)(sizeof(kinds)/sizeof(kinds[0])); k++)
    for (i = 0; i < MAX_OBJECTS; i++)
      if (object_list[i].tval == kinds[k])
        {
          inven_type held;

          invcopy(&held, i);
          if (kinds[k] == TV_ARROW)
            held.number = 20;
          (void) inven_carry(&held);
          break;
        }
}

/* The keys one round needs, padded with escapes. Fed fresh each round so that
   nothing one action leaves behind is read by the next. */
static void moria4_keys(const char *keys)
{
  char script[600];
  int n = 0;
  int i;

  for (i = 0; keys[i]; i++)
    script[n++] = keys[i];
  for (i = n; i < 599; i++)
    script[i] = (char)27;
  script[599] = 0;

  oracle_feed_keys(script);
}

static void dump_moria4(unsigned long seed, int level, int variation)
{
  int i, round;

  header("moria4", seed);
  printf("level %d\n", level);
  printf("variation %d\n", variation);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();

  init_seeds((int32u)seed);
  pin_player(level);
  dun_level = (int16)level;

  init_curses();
  oracle_screen_reset();
  msg_flag = FALSE;

  generate_cave();
  cave[char_row][char_col].cptr = 1;

  py.misc.lev = 20;
  py.misc.expfact = 100;
  py.misc.hitdie = 10;
  py.misc.save = 40;
  py.misc.wt = 150;
  py.flags.food = 5000;

  for (i = 0; i < 6; i++)
    {
      py.stats.max_stat[i] = 18;
      py.stats.cur_stat[i] = 18;
      py.stats.mod_stat[i] = 0;
      set_use_stat(i);
    }

  (void) memset((char *)object_ident, 0, OBJECT_IDENT_SIZE);

  moria4_wield(variation);
  calc_bonuses();

  py.misc.mhp = 2000;
  py.misc.chp = 2000;

  moria4_pack();
  moria4_surround();

  if (variation == 7 || variation == 8 || variation == 9)
    moria4_target(variation == 9);

  player_light = TRUE;
  panel_row = panel_col = -1;
  check_view();

  for (round = 0; round < MORIA4_ROUNDS; round++)
    {
      /* Cleared each round, so that a message left over from the last one does
         not turn this one's prompt into a -more- that eats a scripted key. */
      msg_flag = FALSE;
      free_turn_flag = FALSE;
      new_level_flag = FALSE;
      command_count = 0;

      switch (variation)
        {
        case 0: case 1: case 2:
          /* Dig at each of the sides and corners that hold something. */
          moria4_keys("");
          tunnel("8264793"[round % 7] - '0');
          break;

        case 3:
          /* The trap in the floor is to the south west. */
          moria4_keys("1");
          disarm_trap();
          break;

        case 4:
          /* The chest is to the south east. */
          moria4_keys("3");
          disarm_trap();
          break;

        case 5:
          /* The door is to the north west. */
          moria4_keys("7");
          bash();
          break;

        case 6:
          moria4_keys("3");   /* the chest */
          bash();
          break;

        case 7: case 8:
          {
            char keys[3];

            keys[0] = 'a';
            keys[1] = '6';
            keys[2] = 0;
            moria4_keys(keys);
            throw_object();
          }
          break;

        case 9:
          /* Something alive, one square east. */
          moria4_keys("6");
          bash();
          break;

        default:
          moria4_keys("");
          break;
        }

      printf("round %d free %d chp %d exp %ld at %d %d\n", round,
             free_turn_flag ? 1 : 0, (int)py.misc.chp, (long)py.misc.exp,
             (int)char_row, (int)char_col);
      printf("  str %d con %d dex %d para %d conf %d\n",
             (int)py.stats.cur_stat[A_STR], (int)py.stats.cur_stat[A_CON],
             (int)py.stats.cur_stat[A_DEX], (int)py.flags.paralysis,
             (int)py.flags.confused);
      {
        int hp = 0;
        int m;

        for (m = MIN_MONIX; m < mfptr; m++)
          hp += m_list[m].hp;

        printf("  packed %d weight %d objects %d monsters %d monhp %d\n",
               (int)inven_ctr, (int)inven_weight, (int)(tcptr - MIN_TRIX),
               (int)(mfptr - MIN_MONIX), hp);
      }

      for (i = 0; i < 9; i++)
        {
          int y = char_row + (i / 3) - 1;
          int x = char_col + (i % 3) - 1;

          if (in_bounds(y, x))
            printf("  around %d fval %d tptr %d tval %d p1 %d flags %lu\n", i,
                   (int)cave[y][x].fval, (int)cave[y][x].tptr,
                   cave[y][x].tptr ? (int)t_list[cave[y][x].tptr].tval : 0,
                   cave[y][x].tptr ? (int)t_list[cave[y][x].tptr].p1 : 0,
                   cave[y][x].tptr
                     ? (unsigned long)t_list[cave[y][x].tptr].flags : 0UL);
        }

      printf("  message %s\n", oracle_screen_row(0));
      printf("  state %lu\n", (unsigned long)get_rnd_seed());
    }

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* -------------------------------------------------------------------- look */

/* The enhanced look, with its cone of peripheral vision.

   The level is left exactly as it was generated, and the whole of it is lit and
   remembered with every creature on show, so that whatever the cone reaches is
   worth describing. Every key the look asks for is answered with a space, which
   steps on to the next thing; the screen is dumped afterwards. */
/* A known arrangement around the player, so that every direction the cone is
   pointed has something in it worth describing.

   A level as generated is mostly corridor, whose walls are all granite - and
   granite is only described when it has something in it, so a look down a
   corridor finds nothing at all and proves nothing. */
static void look_arena(void)
{
  static int offsets[8][2] = {
    {-1, 0}, {1, 0}, {0, -1}, {0, 1}, {-1, -1}, {-1, 1}, {1, -1}, {1, 1}
  };
  int i, j, k, slot;

  /* An open floor to look across. */
  for (i = -5; i <= 5; i++)
    for (j = -7; j <= 7; j++)
      {
        int y = char_row + i;
        int x = char_col + j;

        if (in_bounds(y, x))
          {
            cave[y][x].fval = CORR_FLOOR;
            cave[y][x].tptr = 0;
            if (!(i == 0 && j == 0))
              cave[y][x].cptr = 0;
          }
      }

  /* Something to see two squares out in each of the eight directions, and
     something else four squares out. */
  for (k = 0; k < 8; k++)
    for (i = 2; i <= 4; i += 2)
      {
        int y = char_row + offsets[k][0] * i;
        int x = char_col + offsets[k][1] * i;

        if (!in_bounds(y, x))
          continue;

        slot = popt();
        invcopy(&t_list[slot], 30 + (k * 3) + (i / 2));
        cave[y][x].tptr = (int8u)slot;
      }

  /* Mineral veins five out, which only the second pass describes. */
  for (k = 0; k < 8; k++)
    {
      int y = char_row + offsets[k][0] * 5;
      int x = char_col + offsets[k][1] * 5;

      if (in_bounds(y, x))
        cave[y][x].fval = (k & 1) ? MAGMA_WALL : QUARTZ_WALL;
    }

  /* And something alive, three squares to the east. */
  if (in_bounds(char_row, char_col + 3))
    (void) place_monster(char_row, char_col + 3, 20, FALSE);
}

static void dump_look(unsigned long seed, int level, int variation)
{
  int i, j;
  int direction;

  header("look", seed);
  printf("level %d\n", level);
  printf("variation %d\n", variation);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();

  init_seeds((int32u)seed);
  pin_player(level);
  dun_level = (int16)level;

  init_curses();
  oracle_screen_reset();
  msg_flag = FALSE;

  generate_cave();
  cave[char_row][char_col].cptr = 1;

  py.misc.lev = 20;
  py.misc.expfact = 100;
  py.flags.food = 5000;

  for (i = 0; i < 6; i++)
    {
      py.stats.max_stat[i] = 18;
      py.stats.cur_stat[i] = 18;
      py.stats.mod_stat[i] = 0;
      set_use_stat(i);
    }

  py.misc.mhp = 500;
  py.misc.chp = 500;

  for (i = 0; i < cur_height; i++)
    for (j = 0; j < cur_width; j++)
      {
        cave[i][j].pl = TRUE;
        cave[i][j].fm = TRUE;
      }

  look_arena();

  for (i = MIN_MONIX; i < mfptr; i++)
    m_list[i].ml = TRUE;

  player_light = TRUE;
  panel_row = panel_col = -1;
  check_view();

  /* Mineral veins are picked out on the odd variations, which is what makes
     look take a second pass over the rock. */
  highlight_seams = (variation % 2);

  msg_flag = FALSE;

  {
    char script[4001];
    int n;

    for (n = 0; n < 4000; n++)
      script[n] = ' ';
    script[4000] = 0;
    oracle_feed_keys(script);
  }

  /* Directions 1 to 9, with 5 meaning every way at once. */
  direction = (variation / 2) + 1;

  {
    char keys[4002];
    int n;

    keys[0] = (char)('0' + direction);
    for (n = 1; n < 4000; n++)
      keys[n] = ' ';
    keys[4000] = 0;
    oracle_feed_keys(keys);
  }

  /* Every description is overwritten by the next, so the only record of what
     the cone actually found is what was on the message line each time it
     stopped to ask. */
  oracle_log_keys(1);
  look();
  oracle_log_keys(0);

  printf("direction %d seams %d\n", direction, highlight_seams);
  oracle_screen_dump("scr");

  printf("final-state %lu\n", (unsigned long)get_rnd_seed());
}

/* ------------------------------------------------------------------- store */

/* A visit to a shop: the screen, the commands and the haggling.

   The stock is whatever store_init() and two rounds of store_maint() produced,
   which the shops mode already compares, so what is new here is everything that
   happens once the player is inside. The whole screen is dumped afterwards
   along with the gold, the pack, the stock and what the shopkeeper now thinks
   of the player.

   The scripts are typed exactly as a player would type them: a command, a
   letter, then offers ending in a return. Padding with escapes is safe here -
   an escape backs out of whatever is being asked - so a script that runs out
   part-way simply ends the visit rather than spinning. */

/* The scripts a visit runs, typed exactly as a player would type them: a
   command, a letter, then offers ending in a return.

   An offer is only worth making if it is near the price, and the price depends
   on the item, the shopkeeper and the player's charisma - so the numbers are
   written as markers and worked out at the last moment. Each side works them
   out with its own arithmetic, so a disagreement about what something is worth
   shows up as two different scripts and a very loud diff.

     %A  what the shopkeeper opens at, buying
     %B  the most the player would pay, which is where their haggling starts
     %C  half way between the two, and %D half way again
     %O  what the shopkeeper opens at, selling
     %M  the most the player could hope to be paid
     %P  half way between those two
     %L  the letter of the first pack slot this shop will look at

   Padding with escapes is safe - an escape backs out of whatever is being asked
   - so a script that runs out part-way ends the visit rather than spinning. */
static char *store_scripts[] = {
  "",                                  /* walk in and walk out              */
  "b",                                 /* turn the page                     */
  "bb",                                /* and turn it back                  */
  "pa%A\r",                            /* buy at the asking price           */
  "pa%C\r%A\r",                        /* offer the middle, then the asking */
  "pa%B\r%C\r%D\r%A\r",                /* three rounds, then take it        */
  "pa%B\r+1\r\r\r\r\r\r\r\r",          /* haggling upwards by increments    */
  "pa1\r",                             /* an offer that is an insult        */
  "pa1\rpa1\rpa1\rpa1\rpa1\rpa1\r",    /* insults until thrown out          */
  "s%L%O\r",                            /* sell at what is offered           */
  "s%L%P\r%O\r",                        /* ask the middle, then take it      */
  "s%L%M\r%P\r%O\r",                    /* start high and come down          */
  "s%L%M\r-1\r\r\r\r\r\r\r\r",          /* selling down by decrements        */
  "s%L99999\r",                         /* an asking price out of all reason */
  "s%L+0\r10\r",                        /* an increment before any offer     */
  "i",                                 /* the pack commands, from inside    */
  "z"                                  /* a key that is no command at all   */
};

#define STORE_SCRIPTS 17

/* Which pack slot the sell command's "a" picks: the first one this shop is
   willing to look at. */
static int store_first_sellable(int store_num)
{
  int i;

  for (i = 0; i < inven_ctr; i++)
#ifdef MAC
    if (store_buy(store_num, inventory[i].tval))
#else
    if ((*store_buy[store_num])(inventory[i].tval))
#endif
      return i;

  return -1;
}

/* Fills the markers in a script with the prices they stand for. */
static void store_fill_script(char *out, const char *script, int store_num)
{
  int32 buy_ask, buy_floor, buy_middle, buy_near;
  int32 sell_open, sell_hope, sell_middle;
  int sell_slot;
  int n = 0;
  int i;

  buy_ask = 1;
  buy_floor = 1;
  buy_middle = 1;
  buy_near = 1;
  sell_open = 1;
  sell_hope = 1;
  sell_middle = 1;

  if (store[store_num].store_ctr > 0)
    {
      inven_type wanted;
      int32 max_sell, min_sell, cost;
      owner_type *o_ptr = &owners[store[store_num].owner];

      take_one_item(&wanted, &store[store_num].store_inven[0].sitem);
      cost = sell_price(store_num, &max_sell, &min_sell, &wanted);

      buy_ask = max_sell * chr_adj() / 100;
      if (buy_ask <= 0)
        buy_ask = 1;

      buy_floor = cost * (200 - (int)o_ptr->max_inflate) / 100;
      if (buy_floor <= 0)
        buy_floor = 1;

      buy_middle = (buy_floor + buy_ask) / 2;
      buy_near = (buy_middle + buy_ask) / 2;
    }

  sell_slot = store_first_sellable(store_num);
  i = sell_slot;

  if (i >= 0)
    {
      inven_type offered;
      int32 cost;
      owner_type *o_ptr = &owners[store[store_num].owner];

      take_one_item(&offered, &inventory[i]);
      cost = item_value(&offered);

      if (cost < 1)
        cost = 1;

      cost = cost * (200 - chr_adj()) / 100;
      cost = cost * (200 - rgold_adj[o_ptr->owner_race][py.misc.prace]) / 100;

      if (cost < 1)
        cost = 1;

      sell_hope = cost * o_ptr->max_inflate / 100;
      sell_open = cost * (200 - (int)o_ptr->max_inflate) / 100;

      if (sell_open < 1)
        sell_open = 1;

      if (sell_hope < sell_open)
        sell_hope = sell_open;

      sell_middle = (sell_open + sell_hope) / 2;
    }

  for (i = 0; script[i]; i++)
    {
      int32 value = -1;

      if (script[i] == '%' && script[i + 1] == 'L')
        {
          /* A letter rather than a number: which pack slot to offer. */
          out[n++] = (char)('a' + (sell_slot < 0 ? 0 : sell_slot));
          i++;
          continue;
        }

      if (script[i] == '%')
        {
          switch (script[i + 1])
            {
            case 'A': value = buy_ask;     break;
            case 'B': value = buy_floor;   break;
            case 'C': value = buy_middle;  break;
            case 'D': value = buy_near;    break;
            case 'O': value = sell_open;   break;
            case 'M': value = sell_hope;   break;
            case 'P': value = sell_middle; break;
            default:  break;
            }
        }

      if (value >= 0)
        {
          n += sprintf(out + n, "%ld", (long)value);
          i++;
        }
      else
        out[n++] = script[i];
    }

  out[n] = 0;
}

static void dump_store(unsigned long seed, int store_num, int variation)
{
  int i, j;
  char *script;

  header("store", seed);
  printf("store %d\n", store_num);
  printf("variation %d\n", variation);

  probe_init_t_level();
  probe_init_m_level();

  init_seeds((int32u)seed);
  magic_init();
  pin_player(0);
  dun_level = 0;

  /* The doors are locked until the clock has started. */
  turn = 100;

  probe_tlink();
  store_init();
  store_maint();
  store_maint();

  init_curses();
  oracle_screen_reset();
  msg_flag = FALSE;

  generate_cave();
  cave[char_row][char_col].cptr = 1;

  py.misc.lev = 20;
  py.misc.expfact = 100;
  py.misc.au = 5000;
  py.flags.food = 5000;

  for (i = 0; i < 6; i++)
    {
      py.stats.max_stat[i] = 18;
      py.stats.cur_stat[i] = 18;
      py.stats.mod_stat[i] = 0;
      set_use_stat(i);
    }

  /* The charisma is what every price is worked out from, so it is varied
     across the scripts rather than pinned. */
  py.stats.use_stat[A_CHR] = 3 + (variation % 16);

  (void) memset((char *)object_ident, 0, OBJECT_IDENT_SIZE);

  inven_ctr = 0;
  inven_weight = 0;
  equip_ctr = 0;

  invcopy(&inventory[INVEN_WIELD], 30);   /* a stiletto */
  invcopy(&inventory[INVEN_LIGHT], 365);  /* a wooden torch */
  inventory[INVEN_LIGHT].p1 = 5000;
  equip_ctr = 2;

  calc_bonuses();

  py.misc.mhp = 500;
  py.misc.chp = 500;

  /* One of each of a spread of kinds, so that whichever shop is visited has
     something of the player's it is willing to look at. */
  {
    static int kinds[] = {
      TV_SWORD, TV_SOFT_ARMOR, TV_POTION1, TV_SCROLL1, TV_FOOD, TV_WAND,
      TV_PRAYER_BOOK, TV_DIGGING, TV_FLASK, TV_AMULET
    };

    for (j = 0; j < (int)(sizeof(kinds)/sizeof(kinds[0])); j++)
      for (i = 0; i < MAX_OBJECTS; i++)
        if (object_list[i].tval == kinds[j])
          {
            inven_type held;

            invcopy(&held, i);
            (void) inven_carry(&held);
            break;
          }
  }

  msg_flag = FALSE;
  free_turn_flag = FALSE;

  script = store_scripts[variation % STORE_SCRIPTS];

  {
    char filled[256];
    char keys[2001];
    int n = 0;

    store_fill_script(filled, script, store_num);
    /* Printed with the returns spelled out, so the line stays readable and a
       diff points at the offer rather than at a carriage return. */
    printf("script ");
    for (i = 0; filled[i]; i++)
      {
        if (filled[i] == '\r')
          printf("<cr>");
        else
          putchar(filled[i]);
      }
    printf("\n");

    for (i = 0; filled[i]; i++)
      keys[n++] = filled[i];
    for (i = n; i < 2000; i++)
      keys[i] = (char)27;
    keys[2000] = 0;
    oracle_feed_keys(keys);
  }

  enter_store(store_num);

  {
    store_type *s = &store[store_num];

    printf("gold %ld packed %d weight %d\n", (long)py.misc.au, (int)inven_ctr,
           (int)inven_weight);
    printf("owner %d insults %d good %d bad %d open %ld\n", (int)s->owner,
           (int)s->insult_cur, (int)s->good_buy, (int)s->bad_buy,
           (long)s->store_open);
    printf("stock %d\n", (int)s->store_ctr);

    for (j = 0; j < s->store_ctr; j++)
      {
        inven_type *it = &s->store_inven[j].sitem;

        printf("  line %d %d %d %d %d %ld %ld %d %d %d\n", j,
               (int)it->index, (int)it->tval, (int)it->subval,
               (int)it->number, (long)it->cost,
               (long)s->store_inven[j].scost, (int)it->p1, (int)it->name2,
               (int)it->ident);
      }

    for (j = 0; j < inven_ctr; j++)
      {
        bigvtype name;

        objdes(name, &inventory[j], TRUE);
        printf("  pack %d %d %s\n", j, (int)inventory[j].number, name);
      }
  }

  oracle_screen_dump("scr");

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
          "  oracle hallucinate <seed> <level>  the map drawn while hallucinating\n"
          "  oracle light <seed> <level> <steps> <variation>  a walk, lit\n"
          "  oracle walk <seed> <level> <steps> <variation>  scripted steps\n"
          "  oracle run <seed> <level> <direction> <variation>  one run\n"
          "  oracle search <seed> <level> <rounds> <chance>  finding what is hidden\n"
          "  oracle names <seed> <first> <count>  item descriptions\n"
          "  oracle pickup <seed> <level> <steps> <variation>  carrying things\n"
          "  oracle fight <seed> <level> <creature> <rounds>  hitting things\n"
          "  oracle traps <seed> <level> <first> <count>  springing traps\n"
          "  oracle monsters <seed> <level> <turns> <variation>  monster turns\n"
          "  oracle potion <seed> <first> <count>  drinking things\n"
          "  oracle scroll <seed> <level> <first> <count>  reading scrolls\n"
          "  oracle wand <seed> <level> <first> <count>  aiming wands\n"
          "  oracle staff <seed> <level> <first> <count>  using staffs\n"
          "  oracle spell <seed> <level> <first> <count>  casting spells\n"
          "  oracle prayer <seed> <level> <first> <count>  reciting prayers\n"
          "  oracle inven <seed> <variation>  the inventory screens\n"
          "  oracle getitem <seed> <variation>  the prompt that asks which item\n"
          "  oracle moria4 <seed> <level> <variation>  digging, disarming, bashing, throwing\n"
          "  oracle look <seed> <level> <variation>  the cone of peripheral vision\n"
          "  oracle store <seed> <store> <variation>  a visit to a shop\n");
  return 2;
}

int main(int argc, char *argv[])
{
  use_unix_line_endings();

  /* bell() in io.c writes the bell character straight to file descriptor 1,
     which is the same stream the dump goes to - and unbuffered, so it lands
     wherever it likes in the output. Turning the beep off is a player option
     the real game already has, and it leaves everything else bell() does
     alone. */
  sound_beep_flag = FALSE;

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

  if (strcmp(argv[1], "scroll") == 0 || strcmp(argv[1], "wand") == 0
      || strcmp(argv[1], "staff") == 0)
    {
      if (argc != 6)
        return usage();

      dump_device(argv[1], strtoul(argv[2], NULL, 10),
                  (int)strtol(argv[3], NULL, 10),
                  (int)strtol(argv[4], NULL, 10),
                  (int)strtol(argv[5], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "spell") == 0 || strcmp(argv[1], "prayer") == 0)
    {
      if (argc != 6)
        return usage();

      dump_magic(argv[1], strtoul(argv[2], NULL, 10),
                 (int)strtol(argv[3], NULL, 10),
                 (int)strtol(argv[4], NULL, 10),
                 (int)strtol(argv[5], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "inven") == 0 || strcmp(argv[1], "getitem") == 0)
    {
      if (argc != 4)
        return usage();

      if (strcmp(argv[1], "inven") == 0)
        dump_inven(strtoul(argv[2], NULL, 10), (int)strtol(argv[3], NULL, 10));
      else
        dump_getitem(strtoul(argv[2], NULL, 10), (int)strtol(argv[3], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "moria4") == 0 || strcmp(argv[1], "look") == 0)
    {
      if (argc != 5)
        return usage();

      if (strcmp(argv[1], "moria4") == 0)
        dump_moria4(strtoul(argv[2], NULL, 10),
                    (int)strtol(argv[3], NULL, 10),
                    (int)strtol(argv[4], NULL, 10));
      else
        dump_look(strtoul(argv[2], NULL, 10),
                  (int)strtol(argv[3], NULL, 10),
                  (int)strtol(argv[4], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "store") == 0)
    {
      if (argc != 5)
        return usage();

      dump_store(strtoul(argv[2], NULL, 10),
                 (int)strtol(argv[3], NULL, 10),
                 (int)strtol(argv[4], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "potion") == 0)
    {
      if (argc != 5)
        {
          return usage();
        }
      dump_potion(strtoul(argv[2], NULL, 10),
                  (int)strtol(argv[3], NULL, 10),
                  (int)strtol(argv[4], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "monsters") == 0)
    {
      if (argc != 6)
        {
          return usage();
        }
      dump_monsters(strtoul(argv[2], NULL, 10),
                    (int)strtol(argv[3], NULL, 10),
                    (int)strtol(argv[4], NULL, 10),
                    (int)strtol(argv[5], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "fight") == 0)
    {
      if (argc != 6)
        {
          return usage();
        }
      dump_fight(strtoul(argv[2], NULL, 10),
                 (int)strtol(argv[3], NULL, 10),
                 (int)strtol(argv[4], NULL, 10),
                 (int)strtol(argv[5], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "traps") == 0)
    {
      if (argc != 6)
        {
          return usage();
        }
      dump_traps(strtoul(argv[2], NULL, 10),
                 (int)strtol(argv[3], NULL, 10),
                 (int)strtol(argv[4], NULL, 10),
                 (int)strtol(argv[5], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "pickup") == 0)
    {
      if (argc != 6)
        {
          return usage();
        }
      dump_pickup(strtoul(argv[2], NULL, 10),
                  (int)strtol(argv[3], NULL, 10),
                  (int)strtol(argv[4], NULL, 10),
                  (int)strtol(argv[5], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "names") == 0)
    {
      if (argc != 5)
        {
          return usage();
        }
      dump_names(strtoul(argv[2], NULL, 10),
                 (int)strtol(argv[3], NULL, 10),
                 (int)strtol(argv[4], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "search") == 0)
    {
      if (argc != 6)
        {
          return usage();
        }
      dump_search(strtoul(argv[2], NULL, 10),
                  (int)strtol(argv[3], NULL, 10),
                  (int)strtol(argv[4], NULL, 10),
                  (int)strtol(argv[5], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "walk") == 0)
    {
      if (argc != 6)
        {
          return usage();
        }
      dump_walk(strtoul(argv[2], NULL, 10),
                (int)strtol(argv[3], NULL, 10),
                (int)strtol(argv[4], NULL, 10),
                (int)strtol(argv[5], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "run") == 0)
    {
      if (argc != 6)
        {
          return usage();
        }
      dump_run(strtoul(argv[2], NULL, 10),
               (int)strtol(argv[3], NULL, 10),
               (int)strtol(argv[4], NULL, 10),
               (int)strtol(argv[5], NULL, 10));
      return 0;
    }

  if (strcmp(argv[1], "light") == 0)
    {
      if (argc != 6)
        {
          return usage();
        }
      dump_light(strtoul(argv[2], NULL, 10),
                 (int)strtol(argv[3], NULL, 10),
                 (int)strtol(argv[4], NULL, 10),
                 (int)strtol(argv[5], NULL, 10));
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
