#include <stdio.h>
#include <stdlib.h>
#include <signal.h>
#include <sched.h>

void
common_function (int is_thread, int sleep_seconds)
{
	while (1) {
		printf ("COMMON FUNCTION: %d\n", is_thread);
		fflush (stdout);
		sleep (sleep_seconds);
	}
}

int
thread_func (void *data)
{
	common_function (1, 2);
	return 0;
}

int
main (void)
{
	pthread_t *thread;

	printf ("Hello World!\n");
	fflush (stdout);
	pthread_create (&thread, NULL, thread_func, NULL);
	common_function (0, 5);

	return 0;
}
