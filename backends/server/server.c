#include <glib.h>
#include <stdio.h>
#include <sys/stat.h>
#include <sys/wait.h>
#include <sys/ptrace.h>
#include <unistd.h>
#include <errno.h>

#include <server.h>

static gpointer
command_loop (gpointer data)
{
	GIOChannel *channel = data;

	return NULL;
}

static void
send_status_message (GIOChannel *channel, ServerStatusMessageType type, int arg)
{
	ServerStatusMessage message = { type, arg };
	GError *error = NULL;
	GIOStatus status;

	status = g_io_channel_write_chars (channel, (char *) &message, sizeof (message), NULL, &error);
	if (status != G_IO_STATUS_NORMAL)
		g_error (G_STRLOC ": Can't write status message: %s", error->message);
}

static void
child_setup_func (gpointer data)
{
	if (ptrace (PTRACE_TRACEME, getpid (), NULL, NULL) != 0)
		g_error (G_STRLOC ": Can't PTRACE_TRACEME: %s", g_strerror (errno));
}

#define usage() g_error (G_STRLOC ": This program must not be called directly.");

int
main (int argc, char **argv, char **envp)
{
	GThread *thread;
	GIOChannel *command_channel, *status_channel;
	struct stat statb;
	const int command_fd = 4, status_fd = 3;
	int version, pid;
	GSpawnFlags flags;
	GError *error = NULL;

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

	command_channel = g_io_channel_unix_new (command_fd);
	status_channel = g_io_channel_unix_new (status_fd);

	if (g_io_channel_set_encoding (status_channel, NULL, &error) != G_IO_STATUS_NORMAL) {
		g_warning (G_STRLOC ": Can't set encoding on status channel: %s", error->message);
		return 1;
	}

	if (g_io_channel_set_encoding (command_channel, NULL, &error) != G_IO_STATUS_NORMAL) {
		g_warning (G_STRLOC ": Can't set encoding on command channel: %s", error->message);
		return 1;
	}

	g_io_channel_set_buffered (status_channel, FALSE);
	g_io_channel_set_buffered (command_channel, FALSE);

	flags = G_SPAWN_SEARCH_PATH | G_SPAWN_DO_NOT_REAP_CHILD;

	if (!strcmp (argv [3], "0")) {
		if (argc < 6)
			usage ();

		if (!g_spawn_async (argv [4], argv + 5, envp, flags, child_setup_func, NULL, &pid, &error))
			g_error (G_STRLOC ": Can't spawn child: %s", error->message);
	} else {
		if (sscanf (argv [3], "%d", &pid) != 1)
			usage ();

		g_message (G_STRLOC ": %d", pid);

		if (ptrace (PTRACE_ATTACH, pid, NULL, NULL) != 0)
			g_error (G_STRLOC ": Can't attach to process %d: %s", pid, g_strerror (errno));
	}

	thread = g_thread_create (command_loop, command_channel, FALSE, NULL);

	do {
		int ret, status;

		ret = waitpid (pid, &status, WUNTRACED);
		if (ret < 0)
			g_error (G_STRLOC ": Can't waitpid (%d): %s", pid,
				 g_strerror (errno));

		if (WIFSTOPPED (status))
			g_message (G_STRLOC ": Stopped with signal %d (%s)", WSTOPSIG (status),
				   g_strsignal (WSTOPSIG (status)));
		else if (WIFEXITED (status)) {
			g_message (G_STRLOC ": Exited with code %d", WEXITSTATUS (status));
			send_status_message (status_channel, MESSAGE_CHILD_EXITED, WEXITSTATUS (status));
			return 0;
		} else if (WIFSIGNALED (status)) {
			g_message (G_STRLOC ": Caught deadly signal %d (%s)", WTERMSIG (status),
				   g_strsignal (WTERMSIG (status)));
			return 0;
		} else
			g_message (G_STRLOC ": %d - %d", ret, status);

	} while (TRUE);

	return 0;
}
