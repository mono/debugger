#include <server.h>

gboolean
mono_debugger_process_server_message (GIOChannel *channel, SpawnChildMessageFunc child_message_cb)
{
	ServerStatusMessage message;
	GError *error = NULL;
	GIOStatus status;

	status = g_io_channel_read_chars (channel, (char *) &message, sizeof (message), NULL, &error);
	if (status != G_IO_STATUS_NORMAL) {
		g_warning (G_STRLOC ": Can't read status message: %s", error->message);
		return FALSE;
	}

	child_message_cb (message.type, message.arg);
	return TRUE;
}
