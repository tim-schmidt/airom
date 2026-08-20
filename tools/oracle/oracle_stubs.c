/* Stubs for the terminal, scoring and signal layers, so the Umoria 5.6 game
   logic can be linked into a headless oracle.

   The oracle exists to answer "what did the original do", so it links the real
   generate.c, misc1.c, monsters.c and friends unmodified. What it does not need
   is any of io.c, death.c, signals.c, files.c or help.c - those are the files
   full of curses, termios, setuid and flock, and they are precisely the parts
   that made the C hard to build on Windows in the first place. Excluding them
   makes the oracle build cheap; this file supplies the symbols they would have
   provided.

   These are written in K&R style on purpose. externs.h only emits ANSI
   prototypes when LINT_ARGS is defined, which happens solely for the Atari TC
   compiler; the portable build - the one AIrom is derived from - sees
   declarations like "void print();" with an empty parameter list. An ANSI
   definition taking a char cannot match that, because the argument undergoes
   default promotion. Old-style definitions match, and match what the original
   actually compiled.

   io.c is no longer among them: it is compiled against the recording curses in
   fake_curses.c, so the display is compared against the original rather than a
   reimplementation.

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

static void oracle_unexpected(what)
char *what;
{
  fflush(stdout);
  fprintf(stderr, "oracle: unexpected call to %s during a headless run\n", what);
  exit(2);
}

/* ---------------------------------------------------------------- clock */

/* misc1.c and save.c both declare "long time();", which collides with the
   64-bit time_t in modern Windows headers. oracle_shim.h renames the calls to
   this rather than editing the reference sources.

   It returns a constant. The oracle is always given an explicit seed, so the
   clock never feeds the generator; pinning it means anything that does reach
   for the time stays reproducible instead of quietly varying per run. */
long moria_time(where)
long *where;
{
  if (where != (long *)0)
    {
      *where = 0L;
    }
  return 0L;
}

/* localtime, as the 1989 code expects it: a long rather than a time_t.

   misc1.c's check_time() looks up the hour in the play-hours table, so the
   answer has to be a real date. It is fixed rather than taken from the clock,
   for the same reason moria_time is: the oracle has to give the same answer on
   every run. Noon on a Sunday: the default table closes the working hours of
   the week, and a Sunday is open all day. */
struct tm *moria_localtime(where)
long *where;
{
  static struct tm fixed;

  fixed.tm_sec = 0;
  fixed.tm_min = 0;
  fixed.tm_hour = 12;
  fixed.tm_mday = 1;
  fixed.tm_mon = 0;
  fixed.tm_year = 90;
  fixed.tm_wday = 0;
  fixed.tm_yday = 0;
  fixed.tm_isdst = 0;

  return &fixed;
}

/* io.c defines check_input only for the platforms with a poll-style input
   call; the portable build leaves it to the system. Headless there is never
   type-ahead, and returning false also stops dungeon.c's rest loop spinning. */
int check_input(microsec)
int microsec;
{
  return 0;
}

/* --------------------------------------------------------- process control */

/* io.c's shell_out() drops the player to a shell. There is nothing to drop to
   here, and a fork that succeeded would be worse than one that fails. */
int fork()
{
  return -1;
}

int wait(status)
int *status;
{
  return -1;
}

/* ------------------------------------------------------- unix privileges */

/* Umoria was installed setuid so its shared scoreboard could be written by
   any player, and drops those privileges as soon as it starts. Windows has no
   equivalent and the oracle has no scoreboard, so these report an unprivileged
   process and accept every change. */
int getuid() { return 0; }
int getgid() { return 0; }

int setuid(id)
int id;
{
  return 0;
}

int setgid(id)
int id;
{
  return 0;
}

/* ---------------------------------------------------------- unix/unix.c */

/* Fills in the player's name from the login account. A fixed name keeps runs
   reproducible, which is the whole point of the harness, and Windows has no
   equivalent of the getpwuid lookup the original did. */
void user_name(buf)
char *buf;
{
  (void)strcpy(buf, "Oracle");
}

/* index() is the BSD spelling of strchr, which misc3.c still uses and which
   Windows does not provide. */
char *index(s, c)
char *s;
int c;
{
  return strchr(s, c);
}

/* --------------------------------------------------------------- death.c */

void display_scores(show_player)
int show_player;
{ }

int duplicate_character() { return 0; }

int32 total_points() { return 0; }

void exit_game()
{
  fflush(stdout);
  exit(0);
}

/* ------------------------------------------------------------- signals.c */

void nosignals() { }
void signals() { }
void init_signals() { }
void ignore_signals() { }
void default_signals() { }
void restore_signals() { }

/* --------------------------------------------------------------- files.c */

void init_scorefile() { }
void read_times() { }
void print_objects() { }

void helpfile(filename)
char *filename;
{ }

int file_character(filename1)
char *filename1;
{
  return 0;
}

/* ---------------------------------------------------------------- help.c */

void ident_char() { }
