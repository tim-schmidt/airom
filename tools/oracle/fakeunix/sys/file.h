/* BSD's sys/file.h, which Umoria uses for the open() flags. Those do exist on
   Windows, in fcntl.h, so this forwards rather than standing in. */
#ifndef ORACLE_FAKE_SYS_FILE_H
#define ORACLE_FAKE_SYS_FILE_H
#include <fcntl.h>
#endif
