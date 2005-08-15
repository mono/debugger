#include <remoting.h>

gboolean
mono_debugger_remoting_spawn (const gchar **argv, const gchar **envp, gint *child_pid,
			      gint *child_socket, gchar **error)
{
	int fd[2], sv[2];
	int len, ret, open_max, i;

	pipe (fd);
	socketpair (AF_LOCAL, SOCK_STREAM, 0, sv);

	*child_pid = fork ();
	if (*child_pid == 0) {
		gchar *error_message;

		close (0); close (1); close (sv [1]);
		dup2 (sv [0], 0); dup2 (sv [0], 1);

		open_max = sysconf (_SC_OPEN_MAX);
		for (i = 4; i < open_max; i++)
			fcntl (i, F_SETFD, FD_CLOEXEC);

		setsid ();

		execve (argv [0], (char **) argv, (char **) envp);

		error_message = g_strdup_printf ("Cannot exec `%s': %s", argv [0], g_strerror (errno));
		len = strlen (error_message) + 1;
		write (fd [1], &len, sizeof (len));
		write (fd [1], error_message, len);
		_exit (1);
	}

	close (fd [1]);
	close (sv [0]);

	*child_socket = sv [1];

	ret = read (fd [0], &len, sizeof (len));

	if (ret != 0) {
		g_assert (ret == 4);

		*error = g_malloc0 (len);
		read (fd [0], *error, len);
		close (fd [0]);
		return FALSE;
	}

	return TRUE;
}

int
mono_debugger_remoting_setup_server (void)
{
	int fd, null;

	fd = dup (0);
	null = open ("/dev/null", 0);

	close (0); close (1);
	dup2 (2, 1);
	dup2 (null, 0);

	return fd;
}

void
mono_debugger_remoting_kill (int pid, int fd)
{
	close (fd);
	if (pid)
		kill (pid, SIGKILL);
}

int
mono_debugger_remoting_stream_read (int fd, void *buffer, int count)
{
	return read (fd, buffer, count);
}

int
mono_debugger_remoting_stream_write (int fd, const void *buffer, int count)
{
	return write (fd, buffer, count);
}

void
mono_debugger_remoting_poll (int fd, PollFunc func)
{
	struct pollfd fds [] = { { fd, POLLIN | POLLHUP | POLLERR, 0 } };
	int ret;

	while (TRUE) {
		ret = poll (fds, 1, -1);
		if (ret < 0) {
			if (errno == EINTR)
				continue;
			break;
		}

		if (fds [0].revents & (POLLHUP | POLLERR | POLLNVAL))
			break;

		func ();
	}
}
