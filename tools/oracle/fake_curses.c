/* The recording curses that Umoria's io.c draws into.

   See fakeunix/ncurses.h for why this exists: it makes the composed screen
   comparable, so the display layer is verified against the original rather than
   against a reimplementation of it.

   Copyright (C) 2026 AIrom contributors
   Licensed under the GNU General Public License v3 or later. See LICENSE. */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <ncurses.h>

static WINDOW oracle_main_screen;
static WINDOW oracle_spare_screen;
static int oracle_spare_taken = 0;

WINDOW *stdscr = &oracle_main_screen;
WINDOW *curscr = &oracle_main_screen;
int LINES = ORACLE_SCREEN_ROWS;
int COLS = ORACLE_SCREEN_COLS;

static void blank(win)
WINDOW *win;
{
  int r, c;

  for (r = 0; r < ORACLE_SCREEN_ROWS; r++)
    for (c = 0; c < ORACLE_SCREEN_COLS; c++)
      win->cells[r][c] = ' ';
  win->row = 0;
  win->col = 0;
  win->_cury = 0;
  win->_curx = 0;
}

void oracle_screen_reset()
{
  blank(&oracle_main_screen);
  blank(&oracle_spare_screen);
  oracle_spare_taken = 0;
}

/* Trailing blanks are trimmed so a diff points at real differences rather than
   at whitespace nobody can see. */
void oracle_screen_dump(label)
const char *label;
{
  char line[ORACLE_SCREEN_COLS + 1];
  int r, c, end;

  for (r = 0; r < ORACLE_SCREEN_ROWS; r++)
    {
      for (c = 0; c < ORACLE_SCREEN_COLS; c++)
        line[c] = oracle_main_screen.cells[r][c];
      end = ORACLE_SCREEN_COLS;
      while (end > 0 && line[end - 1] == ' ')
        end--;
      line[end] = '\0';
      printf("%s %d %s\n", label, r, line);
    }
}

/* The same, but cut at a column, for screens whose right-hand side is drawn by
   something that is not ported yet. The message line at the top and the status
   line at the bottom are always whole: they belong to the sidebar, not the map.
   */
void oracle_screen_dump_columns(label, width)
const char *label;
int width;
{
  char line[ORACLE_SCREEN_COLS + 1];
  int r, c, end, take;

  for (r = 0; r < ORACLE_SCREEN_ROWS; r++)
    {
      take = (r == 0 || r == ORACLE_SCREEN_ROWS - 1) ? ORACLE_SCREEN_COLS : width;
      for (c = 0; c < take; c++)
        line[c] = oracle_main_screen.cells[r][c];
      end = take;
      while (end > 0 && line[end - 1] == ' ')
        end--;
      line[end] = '\0';
      printf("%s %d %s\n", label, r, line);
    }
}

WINDOW *initscr()
{
  oracle_screen_reset();
  return stdscr;
}

int endwin() { return OK; }

WINDOW *newwin(rows, cols, row, col)
int rows, cols, row, col;
{
  if (oracle_spare_taken)
    return (WINDOW *)0;
  oracle_spare_taken = 1;
  blank(&oracle_spare_screen);
  return &oracle_spare_screen;
}

int overwrite(from, to)
WINDOW *from;
WINDOW *to;
{
  memcpy(to->cells, from->cells, sizeof(from->cells));
  return OK;
}

int touchwin(win) WINDOW *win; { return OK; }
int wclear(win) WINDOW *win; { blank(win); return OK; }
int wrefresh(win) WINDOW *win; { return OK; }
int refresh() { return OK; }

int clear()
{
  blank(stdscr);
  return OK;
}

/* Blank from the cursor to the end of its line. */
int clrtoeol()
{
  int c;

  for (c = stdscr->col; c < ORACLE_SCREEN_COLS; c++)
    stdscr->cells[stdscr->row][c] = ' ';
  return OK;
}

/* Blank from the cursor to the bottom of the screen. */
int clrtobot()
{
  int r, c;

  clrtoeol();
  for (r = stdscr->row + 1; r < ORACLE_SCREEN_ROWS; r++)
    for (c = 0; c < ORACLE_SCREEN_COLS; c++)
      stdscr->cells[r][c] = ' ';
  return OK;
}

/* Out-of-range positions are what io.c checks for: print() and
   move_cursor_relative() abort on ERR, which is how a panel arithmetic error
   announces itself rather than corrupting the display quietly. */
int move(row, col)
int row, col;
{
  if (row < 0 || row >= ORACLE_SCREEN_ROWS || col < 0 || col >= ORACLE_SCREEN_COLS)
    return ERR;
  stdscr->row = row;
  stdscr->col = col;
  stdscr->_cury = row;
  stdscr->_curx = col;
  return OK;
}

int addch(ch)
int ch;
{
  if (stdscr->col >= ORACLE_SCREEN_COLS)
    return ERR;
  stdscr->cells[stdscr->row][stdscr->col] = (char)ch;
  stdscr->col++;
  stdscr->_curx = stdscr->col;
  return OK;
}

int addstr(s)
const char *s;
{
  int i;

  for (i = 0; s[i] != '\0'; i++)
    {
      if (stdscr->col >= ORACLE_SCREEN_COLS)
        return ERR;
      stdscr->cells[stdscr->row][stdscr->col] = s[i];
      stdscr->col++;
    }
  return OK;
}

int mvaddch(row, col, ch)
int row, col, ch;
{
  if (move(row, col) == ERR)
    return ERR;
  return addch(ch);
}

int mvaddstr(row, col, s)
int row, col;
const char *s;
{
  if (move(row, col) == ERR)
    return ERR;
  return addstr(s);
}

/* Scripted keystrokes.

   io.c's own inkey() reads through getch(), so feeding the script in here lets
   the real input path run - including its handling of counts, control keys and
   the -more- prompt - rather than being bypassed. */
static char oracle_keys[4096];
static int oracle_key_count = 0;
static int oracle_key_next = 0;

void oracle_feed_keys(keys)
char *keys;
{
  int i;

  oracle_key_count = 0;
  oracle_key_next = 0;
  for (i = 0; keys[i] != '\0' && i < 4095; i++)
    oracle_keys[oracle_key_count++] = keys[i];
}

/* Some screens are torn down before they can be dumped: screen_map draws the
   whole level, waits for a key, then puts back what was there before. Arming a
   snapshot captures the screen at the moment it asks, which is the only point
   the drawing exists. */
static WINDOW oracle_snapshot;
static int oracle_snapshot_armed = 0;

void oracle_snapshot_next_key()
{
  oracle_snapshot_armed = 1;
}

void oracle_snapshot_dump(label)
const char *label;
{
  char line[ORACLE_SCREEN_COLS + 1];
  int r, c, end;

  for (r = 0; r < ORACLE_SCREEN_ROWS; r++)
    {
      for (c = 0; c < ORACLE_SCREEN_COLS; c++)
        line[c] = oracle_snapshot.cells[r][c];
      end = ORACLE_SCREEN_COLS;
      while (end > 0 && line[end - 1] == ' ')
        end--;
      line[end] = '\0';
      printf("%s %d %s\n", label, r, line);
    }
}

int getch()
{
  if (oracle_snapshot_armed)
    {
      memcpy(oracle_snapshot.cells, oracle_main_screen.cells,
             sizeof(oracle_main_screen.cells));
      oracle_snapshot_armed = 0;
    }

  if (oracle_key_next >= oracle_key_count)
    {
      {
        int c;
        char line[81];
        for (c = 0; c < 80; c++)
          line[c] = oracle_main_screen.cells[0][c];
        line[80] = 0;
        fprintf(stderr, "oracle: message line was [%s]\n", line);
      }
      fflush(stdout);
      fprintf(stderr, "oracle: input script exhausted\n");
      exit(2);
    }
  return (int)(unsigned char)oracle_keys[oracle_key_next++];
}

int wgetch(win) WINDOW *win; { return getch(); }

int cbreak() { return OK; }
int nocbreak() { return OK; }
int echo() { return OK; }
int noecho() { return OK; }
int nl() { return OK; }
int nonl() { return OK; }
int raw() { return OK; }
int noraw() { return OK; }
int savetty() { return OK; }
int resetty() { return OK; }
int scrollok(win, flag) WINDOW *win; int flag; { return OK; }

int mvcur(oldrow, oldcol, row, col)
int oldrow, oldcol, row, col;
{
  return move(row, col);
}

/* The terminal mode calls io.c makes on the way in and out. */
int ioctl() { return 0; }
