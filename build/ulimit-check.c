#include <stdio.h>
#include <sys/time.h>
#include <sys/resource.h>

#define VLIMIT_MINIMUM 1200000

int
main (void)
{
	struct rlimit rlim;
	long vlimit;

	if (getrlimit (RLIMIT_AS, &rlim) != 0) {
		fprintf (stderr, "getrlimit (RLIMIT_AS) failed!\n");
		return -1;
	}

	vlimit = rlim.rlim_cur / 1024;
	if (vlimit < VLIMIT_MINIMUM) {
		fprintf (stderr, "ERROR: 'ulimit -v' is too low, need a minimum of %ld kb.\n", VLIMIT_MINIMUM);
		return -1;
	}

	return 0;
}
