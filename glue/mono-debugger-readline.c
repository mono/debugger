#include "config.h"
#include <mono-debugger-readline.h>
#include <signal.h>
#include <unistd.h>

static gboolean in_readline = FALSE;

static void
sigint_handler (int dummy)
{
	/* do nothing. */
}

void
mono_debugger_readline_static_init (void)
{
	struct sigaction sa;

	sa.sa_handler = sigint_handler;
	sigemptyset (&sa.sa_mask);
	sa.sa_flags = SA_RESTART;

	sigaction (SIGINT, &sa, NULL);
}

int
mono_debugger_readline_is_a_tty (int fd)
{
	return isatty (fd);
}

char *
mono_debugger_readline_readline (const char *prompt)
{
#if USE_READLINE
	char *line;
#else
	char buffer [BUFSIZ];
#endif
	char *retval = NULL;

	g_assert (!in_readline);
	in_readline = TRUE;

#if USE_READLINE
	line = readline (prompt);
	retval = g_strdup (line);
	if (line)
		free (line);
#else
	printf (prompt); fflush (stdout);
	if (fgets (buffer, BUFSIZ, stdin))
		retval = g_strdup (buffer);
#endif

	in_readline = FALSE;
	return retval;
}

void
mono_debugger_readline_add_history (const char *line)
{
#ifdef USE_READLINE
	add_history (line);
#endif
}
