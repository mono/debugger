#include "config.h"
#include <mono-debugger-readline.h>
#include <signal.h>
#include <unistd.h>
#include <stdlib.h>

static gboolean in_readline = FALSE;

#if USE_READLINE
static void
sigint_handler (int dummy)
{
	/* do nothing. */
}
#endif

int
mono_debugger_readline_static_init (void)
{
#if USE_READLINE
	struct sigaction sa;

	sa.sa_handler = sigint_handler;
	sigemptyset (&sa.sa_mask);
	sa.sa_flags = SA_RESTART;

	sigaction (SIGINT, &sa, NULL);

	return TRUE;
#else
	return FALSE;
#endif
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
	char *retval = NULL;

	g_assert (!in_readline);
	in_readline = TRUE;

	line = readline (prompt);
	retval = g_strdup (line);
	if (line)
		free (line);

	in_readline = FALSE;
	return retval;
#else
	return NULL;
#endif
}

void
mono_debugger_readline_add_history (const char *line)
{
#ifdef USE_READLINE
	add_history (line);
#endif
}
