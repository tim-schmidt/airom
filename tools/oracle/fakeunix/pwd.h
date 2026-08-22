/* Enough of <pwd.h> for the oracle.
 *
 * death.c includes it for getpwuid(), which it only uses to put a real name
 * beside a score on a shared Unix machine. There is no such machine here, so
 * the structure exists and nothing fills it in.
 */

#ifndef ORACLE_FAKE_PWD_H
#define ORACLE_FAKE_PWD_H

struct passwd
{
  char *pw_name;
  char *pw_gecos;
  char *pw_dir;
};

#endif
