#include <stdio.h>
#include <errno.h>
#include <wait.h>
#include <semaphore.h>
#include <pthread.h>
#include <sys/ptrace.h>

static pid_t child_pid;
static sem_t start_sem;

static int
thread_func (void *data)
{
	int ret, status;

	// Wait until main thread forked.
	sem_wait (&start_sem);

	ret = waitpid (-1, &status, WUNTRACED | __WALL);

	if (ret == child_pid) {
		fprintf (stderr, "OK\n");
		exit (0);
	} else {
		fprintf (stderr, "Result: %d - %x - %s\n", ret, status, strerror (errno));
		exit (1);
	}
}

int
main (void)
{
	pthread_t thread;

	sem_init (&start_sem, 1, 0);

	// First, we create a thread.
	pthread_create (&thread, NULL, thread_func, NULL);

	// Now let's fork a child and trace it.
	child_pid = fork ();
	if (!child_pid) {
		ptrace (PTRACE_TRACEME, 0, NULL, NULL);
		asm ("int $03");
	}

	// Ok, child created.  Now let our sibling thread wait for it.
	sem_post (&start_sem);

	sleep (500);
}
