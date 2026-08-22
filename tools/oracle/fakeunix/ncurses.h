/* A curses that draws into memory instead of a terminal.

   Umoria's io.c is where every character reaches the screen, and it is the one
   layer the oracle could not previously compare: the output stubs simply threw
   the drawing away. Reimplementing io.c inside the harness would only have
   compared two copies written for this project.

   So io.c is linked unchanged and curses is replaced underneath it. The
   functions here record into a 24x80 grid that the harness can print, which
   makes the composed screen as diffable as everything else - including the
   panel arithmetic in print() and move_cursor_relative(), which is the part
   most likely to be off by one.

   Only the two dozen calls io.c actually makes are provided.

   Copyright (C) 2026 AIrom contributors
   Licensed under the GNU General Public License v3 or later. See LICENSE. */

#ifndef ORACLE_FAKE_CURSES_H
#define ORACLE_FAKE_CURSES_H

#define ERR (-1)
#define OK 0

#define ORACLE_SCREEN_ROWS 24
#define ORACLE_SCREEN_COLS 80

/* A window is just a grid. io.c only ever needs two: the screen itself and one
   spare for save_screen(). */
typedef struct oracle_window
{
  char cells[ORACLE_SCREEN_ROWS][ORACLE_SCREEN_COLS];
  int row;
  int col;
  /* io.c reaches into the window for the cursor, as programs did before
     curses grew accessors. The names are the historical ones. */
  int _cury;
  int _curx;
} WINDOW;

extern WINDOW *stdscr;

/* The physical screen. io.c only ever passes it to mvcur and wrefresh. */
extern WINDOW *curscr;
extern int LINES;
extern int COLS;

extern WINDOW *initscr(void);
extern int endwin(void);
extern WINDOW *newwin(int rows, int cols, int row, int col);
extern int overwrite(WINDOW *from, WINDOW *to);
extern int touchwin(WINDOW *win);
extern int wclear(WINDOW *win);
extern int wrefresh(WINDOW *win);
extern int refresh(void);
extern int clear(void);
extern int clrtoeol(void);
extern int clrtobot(void);
extern int move(int row, int col);
extern int addch(int ch);
extern int addstr(const char *s);
extern int mvaddch(int row, int col, int ch);
extern int mvaddstr(int row, int col, const char *s);
extern int getch(void);
extern int wgetch(WINDOW *win);
extern int cbreak(void);
extern int nocbreak(void);
extern int echo(void);
extern int noecho(void);
extern int nl(void);
extern int nonl(void);
extern int raw(void);
extern int noraw(void);
extern int savetty(void);
extern int resetty(void);
extern int scrollok(WINDOW *win, int flag);
extern int mvcur(int oldrow, int oldcol, int row, int col);

/* getyx has always been a macro; io.c uses it as one. */
#define getyx(win, y, x) ((y) = (win)->_cury, (x) = (win)->_curx)

/* The pre-POSIX spellings of cbreak and nocbreak, which io.c still uses. */
#define crmode() cbreak()
#define nocrmode() nocbreak()

/* Harness hooks, not part of curses. */
extern void oracle_screen_reset(void);
extern void oracle_screen_dump(const char *label);
extern void oracle_snapshot_next_key(void);
extern void oracle_snapshot_dump(const char *label);

#endif /* ORACLE_FAKE_CURSES_H */
