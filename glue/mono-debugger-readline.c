#include <mono-debugger-readline.h>

static gboolean in_readline = FALSE;

static void
event_hook (void)
{
	while (g_main_context_iteration (NULL, FALSE))
		;
}

void
mono_debugger_readline_init ()
{
	rl_event_hook = event_hook;
}

char *
mono_debugger_readline_readline (const char *prompt)
{
	char *retval;

	g_assert (!in_readline);
	in_readline = TRUE;
	retval = readline (prompt);
	in_readline = FALSE;
	return retval;
}

void
mono_debugger_readline_add_history (const char *line)
{
	add_history (line);
}
