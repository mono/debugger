#include "config.h"
#include <mono-debugger-readline.h>
#include <signal.h>
#include <unistd.h>

static gboolean in_readline = FALSE;

#ifdef USE_READLINE
static GIOChannel *readline_channel = NULL;

static int
getc_func (FILE *dummy)
{
	GIOStatus status;
	char ch;
	int count;

	status = g_io_channel_read_chars (readline_channel, &ch, 1, &count, NULL);
	if (status == G_IO_STATUS_EOF)
		return EOF;

	g_assert (status == G_IO_STATUS_NORMAL);
	return ch;
}
#endif

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

#ifdef USE_READLINE
	rl_getc_function = getc_func;
#endif
}

void
mono_debugger_readline_init (GIOChannel *channel)
{
	GIOFlags flags;

	flags = g_io_channel_get_flags (channel);
	g_io_channel_set_flags (channel, flags & ~G_IO_FLAG_NONBLOCK, NULL);

#ifdef USE_READLINE
	g_io_channel_set_encoding (channel, NULL, NULL);
	g_io_channel_set_buffered (channel, FALSE);
#else
	g_io_channel_set_buffered (channel, TRUE);
#endif
}

int
mono_debugger_readline_is_a_tty (int fd)
{
	return isatty (fd);
}

char *
mono_debugger_readline_readline (GIOChannel *channel, const char *prompt)
{
	char *retval;
#ifndef USE_READLINE
	GIOStatus status;
#endif

	g_assert (!in_readline);
	in_readline = TRUE;

#ifdef USE_READLINE
	readline_channel = channel;
	retval = readline (prompt);
	readline_channel = NULL;
#else
	printf (prompt); fflush (stdout);
	status = g_io_channel_read_line (channel, &retval, NULL, NULL, NULL);
	if (status != G_IO_STATUS_NORMAL)
		return NULL;
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
