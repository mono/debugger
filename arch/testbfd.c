#include "bfdglue.h"
#include <signal.h>
#include <string.h>
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
	bfd *abfd;
	asymbol **symtab = NULL;
	void *dis;
	int storage_needed;
	BfdGlueSection *sections;
	int count_sections;

	bfd_init ();

	abfd = bfd_openr ("test", NULL);
	if (!abfd) {
		bfd_perror (NULL);
		return 1;
	}

	if (!bfd_check_format (abfd, bfd_object)) {
		bfd_perror (NULL);
		return 2;
	}

#if 0
	if (!bfd_check_format (abfd, bfd_core)) {
		bfd_perror (NULL);
		return 3;
	}
#endif

	storage_needed = bfd_glue_get_symbols (abfd, &symtab);

	bfd_glue_get_sections (abfd, &sections, &count_sections);

	dis = disassembler (abfd);

	printf ("BFD: %p - %d - %p - %p - %p,%d\n", abfd, storage_needed, symtab,
		dis, sections, count_sections);

	return 0;
}
