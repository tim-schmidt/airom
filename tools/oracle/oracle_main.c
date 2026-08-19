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

#include "config.h"
#include "constant.h"
#include "types.h"
#include "externs.h"

/* Bump when the dump format changes in a way the C# side must match. */
#define ORACLE_FORMAT 1

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

/* ---------------------------------------------------------------- driver */

static int usage(void)
{
  fprintf(stderr,
          "usage:\n"
          "  oracle rng   <seed> <count>   raw generator values\n"
          "  oracle seeds <seed>           seeding chain and magic_init\n"
          "  oracle cave  <seed> <level>   a generated dungeon level\n");
  return 2;
}

int main(int argc, char *argv[])
{
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
