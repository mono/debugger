AC_DEFUN([LINUX_NPTL_CHECK], [
   old_LIBS=$LIBS
   LIBS="$LIBS -lpthread"
   AC_MSG_CHECKING(for NPTL support)
   AC_RUN_IFELSE([
#include <stdio.h>
#include <errno.h>
#include <wait.h>
#include <semaphore.h>
#include <pthread.h>
#include <sys/ptrace.h>

static pid_t child_pid;
static sem_t start_sem;
static sem_t finished_sem;
static int ok = 0;

static int
thread_func (void *data)
{
	int ret, status;

	/* Wait until main thread forked. */
	sem_wait (&start_sem);

	ret = waitpid (-1, &status, WUNTRACED | __WALL);

	if (ret == child_pid) {
		fprintf (stderr, "OK\n");
		ok = 1;
	} else {
		fprintf (stderr, "Result: %d - %x - %s\n", ret, status, strerror (errno));
		ok = 0;
	}

	sem_post (&finished_sem);
	exit (!ok);
}

int
main (void)
{
	pthread_t thread;

	sem_init (&start_sem, 1, 0);
	sem_init (&finished_sem, 1, 0);

	/* First, we create a thread. */
	pthread_create (&thread, NULL, thread_func, NULL);

	/* Now let's fork a child and trace it. */
	child_pid = fork ();
	if (!child_pid) {
		ptrace (PTRACE_TRACEME, 0, NULL, NULL);
		asm ("int $""03");
	}

	/* Ok, child created.  Now let our sibling thread wait for it. */
	sem_post (&start_sem);

	sem_wait (&finished_sem);
	fprintf (stderr, "OK: %d\n", ok);

	exit (!ok);
}], nptl=yes, nptl=no)
   AC_MSG_RESULT($nptl)
   LIBS=$old_LIBS

   if test x$nptl != xyes; then
      AC_ERROR([Your kernel/glibc has no NPTL support.  Please read README.build])
   fi
])
