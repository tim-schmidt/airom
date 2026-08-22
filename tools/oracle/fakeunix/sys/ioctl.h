/* The terminal control Umoria's io.c expects, reduced to nothing.

   io.c saves and restores the terminal mode through BSD sgtty ioctls. None of
   that exists on Windows and none of it matters headlessly, but the calls still
   have to compile - so the structs are declared and ioctl does nothing.

   Copyright (C) 2026 AIrom contributors
   Licensed under the GNU General Public License v3 or later. See LICENSE. */

#ifndef ORACLE_FAKE_IOCTL_H
#define ORACLE_FAKE_IOCTL_H

struct sgttyb { int sg_flags; char sg_erase; char sg_kill; };
struct tchars { char t_intrc, t_quitc, t_startc, t_stopc, t_eofc, t_brkc; };
struct ltchars { char t_suspc, t_dsuspc, t_rprntc, t_flushc, t_werasc, t_lnextc; };

#define TIOCGETP 1
#define TIOCSETP 2
#define TIOCGETC 3
#define TIOCSETC 4
#define TIOCGLTC 5
#define TIOCSLTC 6
#define TIOCLGET 7
#define TIOCLSET 8

#define LPASS8 0

extern int ioctl();

#endif
