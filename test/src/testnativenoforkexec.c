#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
#include <errno.h>
#include <string.h>

int
main (int argc, char **argv)
{
	int ret;

	if (argc < 2)
		return -1;

	ret = execv (argv [1], argv + 1);
	fprintf (stderr, "ERROR: %d - %d (%s)\n", ret, errno, strerror (errno));
	return -1;
}
