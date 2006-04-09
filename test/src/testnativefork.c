#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
#include <sys/wait.h>

int
main (void)
{
	pid_t pid, ret;
	int status;

	pid = fork ();
	if (pid == 0) {
		sleep (1);
		sleep (1);
		exit (0);
	}

	ret = waitpid (pid, &status, 0);
	return 0;
}
