/* BSD's sys/file.h, which Umoria uses for the open() flags and for the seek
   and lock constants around the score file. The open() flags do exist on
   Windows, in fcntl.h, so those are forwarded; the rest are spelled out.

   The lock values are only ever handed to flock(), which this harness supplies
   as a no-op: one process, one score file, nothing to lock against. */
#ifndef ORACLE_FAKE_SYS_FILE_H
#define ORACLE_FAKE_SYS_FILE_H

#include <fcntl.h>
#include <stdio.h>

#ifndef L_SET
#define L_SET  SEEK_SET
#define L_INCR SEEK_CUR
#define L_XTND SEEK_END
#endif

#ifndef LOCK_SH
#define LOCK_SH 1
#define LOCK_EX 2
#define LOCK_NB 4
#define LOCK_UN 8
#endif

#endif
