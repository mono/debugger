#include <mono-debugger-glue.h>
#include <unistd.h>
#include <string.h>
#include <signal.h>
#include <stdio.h>

static gboolean
watch_input_func (GIOChannel *channel, GIOCondition condition, gpointer data)
{
	if (condition == G_IO_IN) {
		char buffer [BUFSIZ];
		GIOStatus status;
		gsize count;

		status = g_io_channel_read_chars (channel, buffer, BUFSIZ, &count, NULL);

		if (status == G_IO_STATUS_NORMAL) {
			buffer [count] = 0;
			((MonoDebuggerGlueReadHandler) data) (buffer);
		}
	}

	return TRUE;
}

static gboolean
watch_hangup_func (GIOChannel *channel, GIOCondition condition, gpointer data)
{
	if (condition == G_IO_HUP) {
		((MonoDebuggerGlueHangupHandler) data) ();
		return FALSE;
	}

	return TRUE;
}

static gboolean
watch_data_input_func (GIOChannel *channel, GIOCondition condition, gpointer data)
{
	((MonoDebuggerGlueEventHandler) data) ();
	return TRUE;
}

unsigned
mono_debugger_glue_add_watch_input (GIOChannel *channel, MonoDebuggerGlueReadHandler cb)
{
	GIOFlags flags = g_io_channel_get_flags (channel);
	g_io_channel_set_flags (channel, flags | G_IO_FLAG_NONBLOCK, NULL);

	return g_io_add_watch (channel, G_IO_IN, watch_input_func, cb);
}

unsigned
mono_debugger_glue_add_watch_hangup (GIOChannel *channel, MonoDebuggerGlueHangupHandler cb)
{
	return g_io_add_watch (channel, G_IO_HUP, watch_hangup_func, cb);
}

void
mono_debugger_glue_add_watch_output (GIOChannel *channel)
{
	GIOFlags flags = g_io_channel_get_flags (channel);
	g_io_channel_set_flags (channel, flags | G_IO_FLAG_NONBLOCK, NULL);
}

void
mono_debugger_glue_setup_data_output (GIOChannel *channel)
{
	g_io_channel_set_encoding (channel, NULL, NULL);
	g_io_channel_set_buffered (channel, FALSE);
}

GSource *
mono_debugger_glue_create_watch_input (GIOChannel *channel, MonoDebuggerGlueEventHandler cb)
{
	GIOFlags flags = g_io_channel_get_flags (channel);
	GSource *source;

	g_io_channel_set_encoding (channel, NULL, NULL);
	g_io_channel_set_buffered (channel, FALSE);

	source = g_io_create_watch (channel, G_IO_IN | G_IO_HUP);
	g_source_set_callback (source, (GSourceFunc) watch_data_input_func, cb, NULL);

	return source;
}

void
mono_debugger_glue_kill_process (int pid, int force)
{
	if (force)
		kill (pid, SIGKILL);
	else
		kill (pid, SIGTERM);
}

void
mono_debugger_glue_write_line (GIOChannel *channel, const char *line)
{
	gsize count;
	GIOStatus status;

	status = g_io_channel_write_chars (channel, line, strlen (line), &count, NULL);
	if ((status != G_IO_STATUS_NORMAL) || (count != strlen (line)))
		g_message (G_STRLOC ": %d - %d - %d", status, strlen (line), count);
	g_io_channel_flush (channel, NULL);
}

int
mono_debugger_glue_read_byte (GIOChannel *channel)
{
	char ch;
	int count;
	GIOStatus status;

	status = g_io_channel_read_chars (channel, &ch, 1, &count, NULL);
	if (status == G_IO_STATUS_NORMAL)
		return (int) ch;
	else
		return -1;
}

int
mono_debugger_glue_write_byte (GIOChannel *channel, int data)
{
	char ch = (char) data;
	int count;
	GIOStatus status;

	status = g_io_channel_write_chars (channel, &ch, 1, &count, NULL);
	if (status == G_IO_STATUS_NORMAL)
		return 0;
	else
		return -1;
}

void
mono_debugger_glue_make_pipe (guint32 *input, guint32 *output)
{
	int fds [2];

	g_assert (pipe (fds) == 0);
	*input = fds [0];
	*output = fds [1];
}

void
mono_debugger_glue_close_pipe (guint32 input, guint32 output)
{
	close (input);
	close (output);
}
