#include <bfdglue.h>
#include <signal.h>
#include <string.h>
#include <link.h>
#include <elf.h>
#ifdef __linux__
#include <sys/user.h>
#include <sys/procfs.h>
#endif
#ifdef __FreeBSD__
#include <sys/param.h>
#include <sys/procfs.h>
#endif

int
main (void)
{
  bfd *bfd= bfd_openr ("testbfd.o", "");

  printf ("BFD: %p\n", bfd);
  return 0;
}
