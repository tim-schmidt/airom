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

   Output stubs are silent. Input stubs abort loudly: nothing in dungeon
   generation should ever ask for a keypress, so if one does, the run is not
   measuring what it claims to and should fail rather than hang.

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

/* ------------------------------------------------------------------ io.c */

void init_curses() { }
void moriaterm() { }
void restore_term() { }
void put_qio() { }
void flush() { }
void clear_screen() { }
void bell() { }
void save_screen() { }
void restore_screen() { }
void screen_map() { }
void shell_out() { }

int suspend() { return 0; }

void put_buffer(out_str, row, col)
char *out_str;
int row, col;
{ }

void erase_line(row, col)
int row, col;
{ }

void clear_from(row)
int row;
{ }

void print(ch, row, col)
char ch;
int row, col;
{ }

void move_cursor_relative(row, col)
int row, col;
{ }

void count_msg_print(p)
char *p;
{ }

void prt(str_buff, row, col)
char *str_buff;
int row, col;
{ }

void move_cursor(row, col)
int row, col;
{ }

void msg_print(str_buff)
char *str_buff;
{ }

void pause_line(prt_line)
int prt_line;
{ }

void pause_exit(prt_line, delay)
int prt_line, delay;
{ }

/* Scripted keystrokes, so the interactive parts of character creation can be
   driven headlessly. Without this the whole of create.c would be unreachable:
   race, sex and class are chosen at a prompt, not passed in. */
static char oracle_keys[64];
static int oracle_key_count = 0;
static int oracle_key_next = 0;

void oracle_feed_keys(keys)
char *keys;
{
  int i;

  oracle_key_count = 0;
  oracle_key_next = 0;
  for (i = 0; keys[i] != '\0' && i < 63; i++)
    {
      oracle_keys[oracle_key_count++] = keys[i];
    }
}

char inkey()
{
  if (oracle_key_next >= oracle_key_count)
    {
      oracle_unexpected("inkey (script exhausted)");
    }
  return oracle_keys[oracle_key_next++];
}

char inkeydir()
{
  oracle_unexpected("inkeydir");
  return 0;
}

int get_check(prompt)
char *prompt;
{
  oracle_unexpected("get_check");
  return 0;
}

int get_com(prompt, command)
char *prompt;
char *command;
{
  oracle_unexpected("get_com");
  return 0;
}

int get_comdir(prompt, command)
char *prompt;
char *command;
{
  oracle_unexpected("get_comdir");
  return 0;
}

/* Character creation asks for a name. Returning false makes it fall back to
   user_name(), which is fixed, so the result stays reproducible. */
int get_string(in_str, row, column, slen)
char *in_str;
int row, column, slen;
{
  in_str[0] = '\0';
  return 0;
}

/* Polls for type-ahead. Headless, there never is any, and returning false also
   stops dungeon.c's rest loop from spinning. */
int check_input(microsec)
int microsec;
{
  return 0;
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
