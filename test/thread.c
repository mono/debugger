#include <stdio.h>
#include <stdlib.h>
#include <signal.h>
#include <sched.h>

int
thread_func (void *data)
{
	while (1) {
		printf ("THREAD FUNC!\n");
		sleep (2);
	}
	return 0;
}

int
main (void)
{
	pthread_t *thread;

	pthread_create (&thread, NULL, thread_func, NULL);

	asm ("int $03");

	while (1) {
		printf ("PARENT FUNC!\n");
		sleep (1);
	}

	return 0;
}
