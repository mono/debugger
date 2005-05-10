#include "config.h"
#include <mono-debugger-readline.h>
#include <signal.h>
#include <unistd.h>
#include <stdlib.h>

static gboolean in_readline = FALSE;

static void
sigint_handler (int dummy)
{
	printf ("Quit\n");
}

void
mono_debugger_readline_static_init (void)
{
	struct sigaction sa;

	sa.sa_handler = sigint_handler;
	sigemptyset (&sa.sa_mask);
	sa.sa_flags = SA_RESTART;

	sigaction (SIGINT, &sa, NULL);

	rl_catch_signals = 1;
	rl_set_signals ();

	rl_readline_name = "mdb";
	rl_terminal_name = getenv ("TERM");
}

int
mono_debugger_readline_is_a_tty (int fd)
{
	return isatty (fd);
}

char *
mono_debugger_readline_readline (const char *prompt)
{
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
}

void
mono_debugger_readline_add_history (const char *line)
{
	add_history (line);
}

char*
mono_debugger_readline_current_line_buffer (void)
{
	return g_strdup (rl_line_buffer);
}

extern int
mono_debugger_readline_get_columns (void)
{
	int cols;

	rl_get_screen_size (NULL, &cols);

	return cols;
}


/* Completion stuff */

int
mono_debugger_readline_get_filename_completion_desired (void)
{
	return rl_filename_completion_desired;
}
    
void
mono_debugger_readline_set_filename_completion_desired (int v)
{
	rl_filename_completion_desired = v;
}

static CompletionDelegate completion_cb;
static char **completion_matches = NULL;

void
mono_debugger_readline_set_completion_matches (char **matches, int count)
{
	int i;

	rl_attempted_completion_over = 1;

	if (count == 0){
		completion_matches = NULL;
		return;
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

void
mono_debugger_readline_enable_completion (CompletionDelegate cb)
{
	rl_attempted_completion_function = mono_debugger_readline_completion_function;

	completion_cb = cb;
}

