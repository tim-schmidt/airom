/* Stubs for the terminal, scoring and signal layers, so the Umoria 5.6 game
   logic can be linked into a headless oracle.

   The oracle exists to answer "what did the original do", so it links the real
   generate.c, misc1.c, monsters.c and friends unmodified. What it does not need
   is any of io.c, death.c, signals.c, files.c or help.c - those are the files
   full of curses, termios, setuid and flock, and they are precisely the parts
   that made the C hard to build on Windows in the first place. Excluding them
   makes the oracle build cheap; this file supplies the symbols they would have
   provided.

   Output stubs are silent. Input stubs abort loudly: nothing in dungeon
   generation should ever ask for a keypress, so if one does, the run is not
   measuring what it claims to and should fail rather than hang.

   Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
   Copyright (C) 2026 AIrom contributors
   Licensed under the GNU General Public License v3 or later. See LICENSE. */

#include <stdio.h>
#include <stdlib.h>

#include "config.h"
#include "constant.h"
#include "types.h"
#include "externs.h"

static void oracle_unexpected(const char *what)
{
  fflush(stdout);
  fprintf(stderr, "oracle: unexpected call to %s during a headless run\n", what);
  exit(2);
}

/* ------------------------------------------------------------------ io.c */

void init_curses(void) { }
void moriaterm(void) { }
void restore_term(void) { }
void put_qio(void) { }
void flush(void) { }
void clear_screen(void) { }
void bell(void) { }
void save_screen(void) { }
void restore_screen(void) { }
void screen_map(void) { }
void shell_out(void) { }

void put_buffer(char *out_str, int row, int col) { (void)out_str; (void)row; (void)col; }
void erase_line(int row, int col) { (void)row; (void)col; }
void clear_from(int row) { (void)row; }
void print(char ch, int row, int col) { (void)ch; (void)row; (void)col; }
void move_cursor_relative(int row, int col) { (void)row; (void)col; }
void count_msg_print(char *p) { (void)p; }
void prt(char *str_buff, int row, int col) { (void)str_buff; (void)row; (void)col; }
void move_cursor(int row, int col) { (void)row; (void)col; }
void msg_print(char *str_buff) { (void)str_buff; }
void pause_line(int prt_line) { (void)prt_line; }
void pause_exit(int prt_line, int delay) { (void)prt_line; (void)delay; }

int suspend(void) { return 0; }

char inkey(void) { oracle_unexpected("inkey"); return 0; }
char inkeydir(void) { oracle_unexpected("inkeydir"); return 0; }

int get_check(char *prompt) { (void)prompt; oracle_unexpected("get_check"); return 0; }

int get_com(char *prompt, char *command)
{
  (void)prompt; (void)command;
  oracle_unexpected("get_com");
  return 0;
}

int get_comdir(char *prompt, char *command)
{
  (void)prompt; (void)command;
  oracle_unexpected("get_comdir");
  return 0;
}

int get_string(char *in_str, int row, int column, int slen)
{
  (void)in_str; (void)row; (void)column; (void)slen;
  oracle_unexpected("get_string");
  return 0;
}

/* --------------------------------------------------------------- death.c */

void display_scores(int show_player) { (void)show_player; }
int duplicate_character(void) { return 0; }
int32 total_points(void) { return 0; }

void exit_game(void)
{
  fflush(stdout);
  exit(0);
}

/* ------------------------------------------------------------- signals.c */

void nosignals(void) { }
void signals(void) { }
void init_signals(void) { }
void ignore_signals(void) { }
void default_signals(void) { }
void restore_signals(void) { }

/* --------------------------------------------------------------- files.c */

void init_scorefile(void) { }
void read_times(void) { }
void helpfile(char *filename) { (void)filename; }
void print_objects(void) { }
int file_character(char *filename1) { (void)filename1; return 0; }

/* ---------------------------------------------------------------- help.c */

void ident_char(void) { }
