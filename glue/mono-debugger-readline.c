#include "config.h"
#include <mono-debugger-readline.h>
#include <signal.h>
#include <unistd.h>
#include <stdlib.h>
#include <mono/metadata/debug-helpers.h>

static gboolean in_readline = FALSE;

#if USE_READLINE
static void
sigint_handler (int dummy)
{
	printf ("Quit\n");
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

char*
mono_debugger_readline_current_line_buffer (void)
{
	return g_strdup (rl_line_buffer);
}


/* Completion stuff */
#ifdef USE_READLINE

static CompletionDelegate completion_cb;
static char **completion_matches = NULL;

void
mono_debugger_readline_set_completion_matches (char **matches, int count)
{
	int i;

	if (completion_matches != NULL) {
		/* we don't free the actual strings, just the array.
		   this is because readline apparently frees the
		   strings itself.  pukey api, if you ask me */
		free (completion_matches);
	}

	completion_matches = (char**)malloc (count * sizeof (char*));
	  
	for (i = 0; i < count; i ++)
		completion_matches[i] = matches[i] ? strdup (matches[i]) : NULL;
}

static char**
mono_debugger_readline_completion_function (const char *text, int start, int end)
{
	completion_matches = NULL;

	if (completion_cb) {
		completion_cb (text, start, end);
	}
	return completion_matches;
}
#endif

void
mono_debugger_readline_enable_completion (CompletionDelegate cb)
{
#ifdef USE_READLINE
	rl_attempted_completion_function = mono_debugger_readline_completion_function;

	completion_cb = cb;
#endif
}

