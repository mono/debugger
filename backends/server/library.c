#include <server.h>
#include <signal.h>
#include <unistd.h>
#include <errno.h>

static gboolean
my_read (ServerHandle *handle, gpointer data, int size)
{
	guint8 *ptr = data;

	while (size) {
		int ret = read (handle->fd, ptr, size);
		if (ret < 0) {
			if (errno == EINTR)
				continue;
			g_warning (G_STRLOC ": Can't read from server (%d): %s (%d)",
				   handle->pid, g_strerror (errno), errno);
			return FALSE;
		}

		size -= ret;
		ptr += ret;
	}

	return TRUE;
}

static ServerCommandError
write_command (ServerHandle *handle, ServerCommand command)
{
	if (write (handle->fd, &command, sizeof (command)) != sizeof (command)) {
		g_warning (G_STRLOC ": Can't write command: %s", g_strerror (errno));
		return COMMAND_ERROR_IO;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
read_status (ServerHandle *handle)
{
	ServerCommandError result;

	if (!my_read (handle, &result, sizeof (result)))
		return COMMAND_ERROR_IO;

	return result;
}

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
mono_debugger_server_send_command (ServerHandle *handle, ServerCommand command)
{
	ServerCommandError result;

	result = write_command (handle, command);
	if (result != COMMAND_ERROR_NONE)
		return result;

	kill (handle->pid, SIGUSR1);

	return read_status (handle);
}

ServerCommandError
mono_debugger_server_read_memory (ServerHandle *handle, guint64 start, guint32 size, gpointer *data)
{
	ServerCommand command = SERVER_COMMAND_READ_DATA;
	ServerCommandError result;

	result = write_command (handle, command);
	if (result != COMMAND_ERROR_NONE)
		return result;

	if (!mono_debugger_server_write_uint64 (handle, start))
		return COMMAND_ERROR_IO;

	if (!mono_debugger_server_write_uint64 (handle, size))
		return COMMAND_ERROR_IO;

	kill (handle->pid, SIGUSR1);

	result = read_status (handle);

	*data = g_malloc (size);

	if (read (handle->fd, *data, size) != size) {
		g_warning (G_STRLOC ": Can't read data: %s", g_strerror (errno));
		return COMMAND_ERROR_IO;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mono_debugger_server_get_target_info (ServerHandle *handle, guint32 *target_int_size,
				      guint32 *target_long_size, guint32 *target_address_size)
{
	ServerCommand command = SERVER_COMMAND_GET_TARGET_INFO;
	ServerCommandError result = mono_debugger_server_send_command (handle, command);
	guint64 arg;

	if (result != COMMAND_ERROR_NONE)
		return result;

	if (!mono_debugger_server_read_uint64 (handle, &arg))
		return COMMAND_ERROR_IO;
	*target_int_size = arg;

	if (!mono_debugger_server_read_uint64 (handle, &arg))
		return COMMAND_ERROR_IO;
	*target_long_size = arg;

	if (!mono_debugger_server_read_uint64 (handle, &arg))
		return COMMAND_ERROR_IO;
	*target_address_size = arg;

	return COMMAND_ERROR_NONE;
}

gboolean
mono_debugger_server_read_uint64 (ServerHandle *handle, guint64 *arg)
{
	if (read (handle->fd, arg, sizeof (*arg)) != sizeof (*arg)) {
		g_warning (G_STRLOC ": Can't read uint64 argument: %s", g_strerror (errno));
		return FALSE;
	}

	return TRUE;
}

gboolean
mono_debugger_server_write_uint64 (ServerHandle *handle, guint64 arg)
{
	if (write (handle->fd, &arg, sizeof (arg)) != sizeof (arg)) {
		g_warning (G_STRLOC ": Can't write uint64 argument: %s", g_strerror (errno));
		return FALSE;
	}

	return TRUE;
}
