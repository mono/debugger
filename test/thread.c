#include <stdio.h>
#include <stdlib.h>
#include <signal.h>
#include <sched.h>
#include <pthread.h>

void
hello (int is_thread)
{
	printf ("HELLO: %x\n", pthread_self ());
	fflush (stdout);
}

void
do_sleep (int seconds)
{
	while (seconds > 0)
		seconds = sleep (seconds);
}

void
common_function (int is_thread, int sleep_seconds)
{
	while (1) {
		do_sleep (sleep_seconds);
		hello (is_thread);
	}
}

int
thread_func (void *data)
{
	asm ("int $03");
	common_function (1, 1);
	return 0;
}

int
main (void)
{
	pthread_t *thread, *thread2;

	printf ("Hello World!\n");
	fflush (stdout);
	pthread_create (&thread, NULL, thread_func, NULL);
	pthread_create (&thread2, NULL, thread_func, NULL);
	asm ("int $03");
	common_function (0, 10);

	return 0;
}
