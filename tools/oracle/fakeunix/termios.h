/* Terminal control, reduced to nothing.

   config.h defines DEBIAN_LINUX, which sends io.c down a termios path. None of
   it means anything headlessly, but it still has to compile.

   Copyright (C) 2026 AIrom contributors
   Licensed under the GNU General Public License v3 or later. See LICENSE. */

#ifndef ORACLE_FAKE_TERMIOS_H
#define ORACLE_FAKE_TERMIOS_H

typedef unsigned int tcflag_t;
typedef unsigned char cc_t;

#define NCCS 32

struct termios
{
  tcflag_t c_iflag, c_oflag, c_cflag, c_lflag;
  cc_t c_line;
  cc_t c_cc[NCCS];
};

#define TCSAFLUSH 2
#define TCSANOW 0

#define ICANON 0002
#define ECHO   0010
#define ISIG   0001
#define VMIN   6
#define VTIME  5
#define VEOL   11
#define VEOL2  16

extern int tcgetattr();
extern int tcsetattr();

#endif
