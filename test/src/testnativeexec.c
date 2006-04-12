#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
#include <errno.h>
#include <string.h>

int
main (void)
{
	pid_t pid, ret;
	int status;

	pid = fork ();
	if (pid == 0) {
		const char *filename = TEST_BUILDDIR "/testnativechild";
		const char *argv [2] = { filename, NULL };
		int ret;

		sleep (1);
		sleep (1);
		ret = execl (filename, filename, NULL);
		fprintf (stderr, "ERROR: %d - %d (%s)\n", ret, errno, strerror (errno));
		return 0;
	}

	ret = waitpid (pid, &status, 0);
	return 0;
}
