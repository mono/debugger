#include <mono-debugger-readline.h>
#include <signal.h>

static gboolean in_readline = FALSE;
static GIOChannel *readline_channel = NULL;

static int
getc_func (FILE *dummy)
{
	GIOStatus status;
	GIOFlags flags;
	char ch;
	int count;

	flags = g_io_channel_get_flags (readline_channel);
	g_io_channel_set_flags (readline_channel, flags & ~G_IO_FLAG_NONBLOCK, NULL);

	status = g_io_channel_read_chars (readline_channel, &ch, 1, &count, NULL);
	if (status == G_IO_STATUS_EOF)
		return EOF;

	g_assert (status == G_IO_STATUS_NORMAL);
	return ch;
}

static void
sigint_handler (int dummy)
{
	/* do nothing. */
}

void
mono_debugger_readline_init (void)
{
	struct sigaction sa;

	sa.sa_handler = sigint_handler;
	sigemptyset (&sa.sa_mask);
	sa.sa_flags = SA_RESTART;

	sigaction (SIGINT, &sa, NULL);

	rl_getc_function = getc_func;
}

char *
mono_debugger_readline_readline (GIOChannel *channel, const char *prompt)
{
	char *retval;

	g_assert (!in_readline);
	in_readline = TRUE;

	readline_channel = channel;
	retval = readline (prompt);
	readline_channel = NULL;

	in_readline = FALSE;
	return retval;
}

void
mono_debugger_readline_add_history (const char *line)
{
	add_history (line);
}
