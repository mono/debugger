#include <server.h>
#include <signal.h>
#include <unistd.h>
#include <errno.h>

gboolean
mono_debugger_process_server_message (ServerHandle *handle)
{
	ServerStatusMessage message;
	GError *error = NULL;
	GIOStatus status;

	status = g_io_channel_read_chars (handle->status_channel, (char *) &message,
					  sizeof (message), NULL, &error);
	if (status != G_IO_STATUS_NORMAL) {
		g_warning (G_STRLOC ": Can't read status message: %s", error->message);
		return FALSE;
	}

	handle->child_message_cb (message.type, message.arg);
	return TRUE;
}

ServerCommandError
mono_debugger_server_command (ServerHandle *handle, ServerCommand command)
{
	ServerCommandError result;

	if (write (handle->fd, &command, sizeof (command)) != sizeof (command)) {
		g_warning (G_STRLOC ": Can't send command: %s", g_strerror (errno));
		return COMMAND_ERROR_IO;
	}

	kill (handle->pid, SIGUSR1);

	if (read (handle->fd, &result, sizeof (result)) != sizeof (result)) {
		g_warning (G_STRLOC ": Can't read command status: %s", g_strerror (errno));
		return COMMAND_ERROR_IO;
	}

	return result;
}
