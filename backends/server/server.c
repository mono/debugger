#include <glib.h>
#include <stdio.h>
#include <sys/stat.h>
#include <sys/wait.h>
#include <unistd.h>
#include <errno.h>

#include <server.h>

static int command_available = 0;
static int shutdown = 0;

static void
write_result (int fd, ServerCommandError result)
{
	if (!mono_debugger_util_write (fd, &result, sizeof (result)))
		g_error (G_STRLOC ": Can't send command status: %s", g_strerror (errno));
}

static void
write_arg (int fd, guint64 arg)
{
	if (!mono_debugger_util_write (fd, &arg, sizeof (arg)))
		g_error (G_STRLOC ": Can't send command argument: %s", g_strerror (errno));
}

static void
command_func (InferiorInfo *info, InferiorHandle *handle, int fd)
{
	ServerCommand command;
	ServerCommandError result;
	guint64 arg, arg2, arg3;
	guint32 iarg, iarg2;
	gpointer data;

	if (!mono_debugger_util_read (fd, &command, sizeof (command)))
		g_error (G_STRLOC ": Can't read command: %s", g_strerror (errno));

	switch (command) {
	case SERVER_COMMAND_GET_PC:
		result = (* info->get_pc) (handle, &arg);
		if (result != COMMAND_ERROR_NONE)
			break;

		write_result (fd, result);
		write_arg (fd, arg);
		return;

	case SERVER_COMMAND_DETACH:
		result = (* info->detach) (handle);
		break;

	case SERVER_COMMAND_SHUTDOWN:
		shutdown = 1;
		result = COMMAND_ERROR_NONE;
		break;

	case SERVER_COMMAND_KILL:
		shutdown = 2;
		result = COMMAND_ERROR_NONE;
		break;

	case SERVER_COMMAND_CONTINUE:
		result = (* info->run) (handle);
		break;

	case SERVER_COMMAND_STEP:
		result = (* info->step) (handle);
		break;

	case SERVER_COMMAND_READ_DATA:
		if (!mono_debugger_util_read (fd, &arg, sizeof (arg)))
			g_error (G_STRLOC ": Can't read arg: %s", g_strerror (errno));
		if (!mono_debugger_util_read (fd, &arg2, sizeof (arg2)))
			g_error (G_STRLOC ": Can't read command: %s", g_strerror (errno));
		data = g_malloc (arg2);
		result = (* info->read_data) (handle, arg, arg2, data);
		if (result != COMMAND_ERROR_NONE) {
			g_free (data);
			break;
		}
		write_result (fd, COMMAND_ERROR_NONE);
		if (!mono_debugger_util_write (fd, data, arg2))
			g_error (G_STRLOC ": Can't send command argument: %s", g_strerror (errno));
		g_free (data);
		return;

	case SERVER_COMMAND_WRITE_DATA:
		if (!mono_debugger_util_read (fd, &arg, sizeof (arg)))
			g_error (G_STRLOC ": Can't read arg: %s", g_strerror (errno));
		if (!mono_debugger_util_read (fd, &arg2, sizeof (arg2)))
			g_error (G_STRLOC ": Can't read command: %s", g_strerror (errno));
		data = g_malloc (arg2);
		if (!mono_debugger_util_read (fd, data, arg2))
			g_error (G_STRLOC ": Can't read command argument: %s", g_strerror (errno));
		result = (* info->write_data) (handle, arg, arg2, data);
		g_free (data);
		break;

	case SERVER_COMMAND_GET_TARGET_INFO:
		write_result (fd, COMMAND_ERROR_NONE);
		write_arg (fd, sizeof (gint32));
		write_arg (fd, sizeof (gint64));
		write_arg (fd, sizeof (void *));
		return;

	case SERVER_COMMAND_CALL_METHOD:
		if (!mono_debugger_util_read (fd, &arg, sizeof (arg)))
			g_error (G_STRLOC ": Can't read arg: %s", g_strerror (errno));
		if (!mono_debugger_util_read (fd, &arg2, sizeof (arg2)))
			g_error (G_STRLOC ": Can't read arg: %s", g_strerror (errno));
		if (!mono_debugger_util_read (fd, &arg3, sizeof (arg3)))
			g_error (G_STRLOC ": Can't read arg: %s", g_strerror (errno));
		result = (* info->call_method) (handle, arg, arg2, arg3);
		break;

	case SERVER_COMMAND_INSERT_BREAKPOINT:
		if (!mono_debugger_util_read (fd, &arg, sizeof (arg)))
			g_error (G_STRLOC ": Can't read arg: %s", g_strerror (errno));
		result = (* info->insert_breakpoint) (handle, arg, &iarg2);
		if (result != COMMAND_ERROR_NONE)
			break;
		write_result (fd, COMMAND_ERROR_NONE);
		if (!mono_debugger_util_write (fd, &iarg2, sizeof (iarg2)))
			g_error (G_STRLOC ": Can't send command argument: %s", g_strerror (errno));
		return;

	case SERVER_COMMAND_REMOVE_BREAKPOINT:
		if (!mono_debugger_util_read (fd, &iarg, sizeof (iarg)))
			g_error (G_STRLOC ": Can't read arg: %s", g_strerror (errno));
		result = (* info->remove_breakpoint) (handle, iarg);
		break;

	default:
		result = COMMAND_ERROR_INVALID_COMMAND;
		break;
	}

	write_result (fd, result);
}

static void
send_status_message (GIOChannel *channel, ServerStatusMessageType type, int arg)
{
	ServerStatusMessage message = { type, arg };
	GError *error = NULL;
	GIOStatus status;

	status = g_io_channel_write_chars (channel, (char *) &message, sizeof (message), NULL, &error);
	if (status != G_IO_STATUS_NORMAL)
		g_error (G_STRLOC ": Can't send status message: %s", error->message);
}

static void
send_callback_message (GIOChannel *channel, guint64 callback, guint64 data)
{
	ServerStatusMessage message = { MESSAGE_CHILD_CALLBACK, 0 };
	GError *error = NULL;
	GIOStatus status;

	status = g_io_channel_write_chars (channel, (char *) &message, sizeof (message), NULL, &error);
	if (status != G_IO_STATUS_NORMAL)
		g_error (G_STRLOC ": Can't send status message: %s", error->message);

	status = g_io_channel_write_chars (channel, (char *) &callback, sizeof (callback), NULL, &error);
	if (status != G_IO_STATUS_NORMAL)
		g_error (G_STRLOC ": Can't send callback argument: %s", error->message);

	status = g_io_channel_write_chars (channel, (char *) &data, sizeof (data), NULL, &error);
	if (status != G_IO_STATUS_NORMAL)
		g_error (G_STRLOC ": Can't send callback argument: %s", error->message);
}

static void
child_setup_func (gpointer data)
{
	(* ((InferiorInfo *) data)->traceme) (getpid ());
}

#define usage() g_error (G_STRLOC ": This program must not be called directly.");

static void
command_signal_handler (int dummy)
{
	command_available = TRUE;
}

static void
signal_handler (int dummy)
{
	/* Do nothing.  This is just to wake us up. */
}

static void
sigterm_handler (int dummy)
{
	shutdown = 2;
}

int
main (int argc, char **argv, char **envp)
{
	InferiorInfo *info = &i386_linux_ptrace_inferior;
	InferiorHandle *handle = NULL;
	GIOChannel *status_channel;
	struct stat statb;
	const int command_fd = 4, status_fd = 3;
	int version, pid, attached;
	GSpawnFlags flags;
	GError *error = NULL;
	sigset_t mask;

	if (argc < 4)
		usage ();

	if (strcmp (argv [1], MONO_SYMBOL_FILE_MAGIC))
		usage ();
	if (sscanf (argv [2], "%d", &version) != 1)
		usage ();

	if (version != MONO_SYMBOL_FILE_VERSION)
		g_error (G_STRLOC ": Incorrect server version; this is %d, but our caller expects %d.",
			 MONO_SYMBOL_FILE_VERSION, version);

	g_thread_init (NULL);

	if (fstat (command_fd, &statb)) {
		g_warning (G_STRLOC ": Can't fstat (%d): %s", command_fd, g_strerror (errno));
		usage ();
	}

	if (fstat (status_fd, &statb)) {
		g_warning (G_STRLOC ": Can't fstat (%d): %s", status_fd, g_strerror (errno));
		usage ();
	}

	status_channel = g_io_channel_unix_new (status_fd);
	if (g_io_channel_set_encoding (status_channel, NULL, &error) != G_IO_STATUS_NORMAL)
		g_error (G_STRLOC ": Can't set encoding on status channel: %s", error->message);
	g_io_channel_set_buffered (status_channel, FALSE);

	flags = G_SPAWN_SEARCH_PATH | G_SPAWN_DO_NOT_REAP_CHILD;

	if (!strcmp (argv [3], "0")) {
		if (argc < 6)
			usage ();

		if (!g_spawn_async (argv [4], argv + 5, envp, flags, child_setup_func, info, &pid, &error))
			g_error (G_STRLOC ": Can't spawn child: %s", error->message);

		handle = (* info->initialize) (pid);
		attached = FALSE;
	} else {
		if (sscanf (argv [3], "%d", &pid) != 1)
			usage ();

		handle = (* info->attach) (pid);
		attached = TRUE;
	}

	/* Get the current signal mask and remove the ones we want to wait for. */
	sigemptyset (&mask);
	sigprocmask (SIG_BLOCK, NULL, &mask);
	sigdelset (&mask, SIGUSR1);
	sigdelset (&mask, SIGCHLD);
	sigdelset (&mask, SIGTERM);

	/* Install our signal handlers. */
	signal (SIGUSR1, command_signal_handler);
	signal (SIGCHLD, signal_handler);
	signal (SIGTERM, sigterm_handler);

	while (!shutdown) {
		int ret, status;

		ret = waitpid (0, &status, WNOHANG | WUNTRACED);
		if (ret < 0)
			g_error (G_STRLOC ": Can't waitpid (%d): %s", pid, g_strerror (errno));

		if (ret != 0) {
			if (WIFSTOPPED (status)) {
				guint64 callback_arg, retval;
				if (! (* info->child_stopped)
				    (handle, WSTOPSIG (status), &callback_arg, &retval))
					send_status_message (status_channel, MESSAGE_CHILD_STOPPED,
							     WSTOPSIG (status));
				else
					send_callback_message (status_channel, callback_arg, retval);
			} else if (WIFEXITED (status)) {
				send_status_message (status_channel, MESSAGE_CHILD_EXITED,
						     WEXITSTATUS (status));
				return 0;
			} else if (WIFSIGNALED (status)) {
				send_status_message (status_channel, MESSAGE_CHILD_SIGNALED,
						     WTERMSIG (status));
				return 0;
			} else
				g_message (G_STRLOC ": %d - %d", ret, status);

			continue;
		}

		/* Wait for a signal to arrive. */
		command_available = FALSE;
		sigsuspend (&mask);

		if (command_available)
			command_func (info, handle, command_fd);
	}

	/* If we attached to a running process, detach from it, otherwise kill it. */
	if (attached)
		(* info->detach) (handle);
	else if (shutdown == 2)
		kill (pid, SIGKILL);
	else
		kill (pid, SIGTERM);

	return 0;
}
