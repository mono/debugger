AC_DEFUN([READLINE_TRYLINK], [
    lib="$1"

    old_LIBS=$LIBS
    LIBS="-l$lib"

    AC_TRY_LINK(,[rl_set_signals();],[READLINE_DEPLIBS=$LIBS],[
		LIBS="-l$lib -ltermcap"
		AC_TRY_LINK(,[rl_set_signals();],[
			READLINE_DEPLIBS=$LIBS
		],[
			LIBS="-l$lib -lcurses"
			AC_TRY_LINK(,[rl_set_signals();],[
				READLINE_DEPLIBS=$LIBS
			],[
				LIBS="-l$lib -lncurses"
				AC_TRY_LINK(,[rl_set_signals();],[
					READLINE_DEPLIBS=$LIBS
				],[
					READLINE_DEPLIBS=
				])
			])
		])
    ])

    LIBS=$old_LIBS
])

AC_DEFUN([CHECK_READLINE], [
        AC_ARG_WITH(readline,   [  -with-readline=[no/yes/libedit]    Enable readline support (default=yes)])
	AC_CACHE_CHECK([for Readline], ac_cv_with_readline, ac_cv_with_readline="${with_readline:=yes}")
	case $ac_cv_with_readline in
	no|"")
		with_readline=no
		;;
	yes)
		with_readline=yes
		;;
	libedit)
		with_readline=libedit;
		;;
	esac

	if test "$with_readline" != no; then
	   READLINE_DEPLIBS=
	   if test "$with_readline" == yes; then
	      READLINE_TRYLINK(readline)
	   fi

	   # fall through to checking for libedit if we didn't find
	   # libreadline (or if you user specified libedit)
	   if test -z "$READLINE_DEPLIBS"; then
	      READLINE_TRYLINK(edit)

	      AC_DEFINE(READLINE_IS_LIBEDIT,1,[if we're using the readline api from libedit])
	   fi

	   if test -z "$READLINE_DEPLIBS"; then
	      AC_MSG_ERROR([Cannot figure out how to link with the readline/libedit library; see config.log for more information])
	   fi
	fi
])

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