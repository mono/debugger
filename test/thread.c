#include <stdio.h>
#include <stdlib.h>
#include <signal.h>
#include <sched.h>
#include <pthread.h>

void
do_sleep (int seconds)
{
	printf ("Sleep: %d\n", seconds);
	while (seconds > 0)
		seconds = sleep (seconds);
	printf ("Done sleeping: %d\n", seconds);
}

void
common_function (int seconds)
{
	while (1) {
		printf ("Looping: %d\n", seconds);
		do_sleep (seconds);
	}
}

int
thread_func (void *data)
{
	common_function (5);
	return 0;
}

int
main (void)
{
	pthread_t *thread;

	pthread_create (&thread, NULL, thread_func, NULL);
	common_function (15);

	return 0;
}
