#define _GNU_SOURCE
#include <server.h>
#include <breakpoints.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/stat.h>
#include <sys/ptrace.h>
#include <sys/socket.h>
#include <sys/wait.h>
#include <sys/poll.h>
#include <sys/select.h>
#include <signal.h>
#include <unistd.h>
#include <string.h>
#include <fcntl.h>
#include <errno.h>

/*
 * NOTE:  The manpage is wrong about the POKE_* commands - the last argument
 *        is the data (a word) to be written, not a pointer to it.
 *
 * In general, the ptrace(2) manpage is very bad, you should really read
 * kernel/ptrace.c and arch/i386/kernel/ptrace.c in the Linux source code
 * to get a better understanding for this stuff.
 */

#ifdef __linux__
#include "i386-linux-ptrace.h"
#endif

#ifdef __FreeBSD__
#include "i386-freebsd-ptrace.h"
#endif

#include "i386-arch.h"

struct InferiorHandle
{
	int pid;
#ifdef __linux__
	int mem_fd;
#endif
	int last_signal;
	int output_fd [2], error_fd [2];
	ChildOutputFunc stdout_handler, stderr_handler;
	int is_thread;
};

void
mono_debugger_server_finalize (ServerHandle *handle)
{
	if (handle->inferior->pid) {
		ptrace (PT_KILL, handle->inferior->pid, NULL, 0);
		ptrace (PT_DETACH, handle->inferior->pid, NULL, 0);
		kill (handle->inferior->pid, SIGKILL);
	}
	i386_arch_finalize (handle->arch);
	g_free (handle->inferior);
	g_free (handle);
}

ServerCommandError
mono_debugger_server_continue (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;

	errno = 0;
	if (ptrace (PT_CONTINUE, inferior->pid, (caddr_t) 1, inferior->last_signal)) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;

		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mono_debugger_server_step (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;

	errno = 0;
	if (ptrace (PT_STEP, inferior->pid, (caddr_t) 1, inferior->last_signal)) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;

		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mono_debugger_server_detach (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;

	if (ptrace (PT_DETACH, inferior->pid, NULL, 0)) {
		g_message (G_STRLOC ": %d - %s", inferior->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mono_debugger_server_kill (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;

	if (inferior->pid)
		kill (inferior->pid, SIGKILL);
	return COMMAND_ERROR_NONE;
}

ServerCommandError
mono_debugger_server_peek_word (ServerHandle *handle, guint64 start, guint32 *retval)
{
	return mono_debugger_server_read_memory (handle, start, sizeof (int), retval);
}

ServerCommandError
mono_debugger_server_write_memory (ServerHandle *handle, guint64 start,
				   guint32 size, gconstpointer buffer)
{
	InferiorHandle *inferior = handle->inferior;
	ServerCommandError result;
	const int *ptr = buffer;
	int addr = start;
	char temp [4];

	while (size >= 4) {
		int word = *ptr++;

		errno = 0;
		if (ptrace (PT_WRITE_D, inferior->pid, (gpointer) addr, word) != 0) {
			if (errno == ESRCH)
				return COMMAND_ERROR_NOT_STOPPED;
			else if (errno) {
				g_message (G_STRLOC ": %d - %s", inferior->pid, g_strerror (errno));
				return COMMAND_ERROR_UNKNOWN;
			}
		}

		addr += sizeof (int);
		size -= sizeof (int);
	}

	if (!size)
		return COMMAND_ERROR_NONE;

	result = mono_debugger_server_read_memory (handle, (guint32) addr, 4, &temp);
	if (result != COMMAND_ERROR_NONE)
		return result;
	memcpy (&temp, ptr, size);

	return mono_debugger_server_write_memory (handle, (guint32) addr, 4, &temp);
}	


static gboolean
dispatch_event (ServerHandle *handle, int status, ServerStatusMessageType *type,
		guint64 *arg, guint64 *data1, guint64 *data2)
{
	if (WIFSTOPPED (status)) {
		guint64 callback_arg, retval, retval2;
		ChildStoppedAction action = i386_arch_child_stopped
			(handle, WSTOPSIG (status), &callback_arg, &retval, &retval2);

		switch (action) {
		case STOP_ACTION_SEND_STOPPED:
			*type = MESSAGE_CHILD_STOPPED;
			if (WSTOPSIG (status) == SIGTRAP)
				*arg = 0;
			else {
				handle->inferior->last_signal = WSTOPSIG (status);
				*arg = WSTOPSIG (status);
			}
			return TRUE;

		case STOP_ACTION_BREAKPOINT_HIT:
			*type = MESSAGE_CHILD_HIT_BREAKPOINT;
			*arg = (int) retval;
			return TRUE;

		case STOP_ACTION_CALLBACK:
			*type = MESSAGE_CHILD_CALLBACK;
			*arg = callback_arg;
			*data1 = retval;
			*data2 = retval2;
			return TRUE;

		default:
			g_assert_not_reached ();
		}
	} else if (WIFEXITED (status)) {
		*type = MESSAGE_CHILD_EXITED;
		*arg = WEXITSTATUS (status);
		handle->inferior->pid = 0;
		return TRUE;
	} else if (WIFSIGNALED (status)) {
		*type = MESSAGE_CHILD_SIGNALED;
		*arg = WTERMSIG (status);
		handle->inferior->pid = 0;
		return TRUE;
	}

	g_warning (G_STRLOC ": Got unknown waitpid() result: %d", status);
	return FALSE;
}

void
mono_debugger_server_wait (ServerHandle *handle, ServerStatusMessageType *type,
			   guint64 *arg, guint64 *data1, guint64 *data2)
{
	int status;

	do {
		status = _mono_debugger_server_wait (handle->inferior);
		if (status == -1)
			return;

	} while (!dispatch_event (handle, status, type, arg, data1, data2));
}

static gboolean initialized = FALSE;
static sigset_t mono_debugger_signal_mask;

ServerHandle *
mono_debugger_server_initialize (BreakpointManager *bpm)
{
	ServerHandle *handle = g_new0 (ServerHandle, 1);

	if (!initialized) {
		/* These signals are only unblocked by sigwait(). */
		sigemptyset (&mono_debugger_signal_mask);
		sigaddset (&mono_debugger_signal_mask, SIGCHLD);
		sigaddset (&mono_debugger_signal_mask, SIGINT);
		sigaddset (&mono_debugger_signal_mask, SIGIO);
		sigprocmask (SIG_BLOCK, &mono_debugger_signal_mask, NULL);

		initialized = TRUE;
	}

	handle->bpm = bpm;
	handle->inferior = g_new0 (InferiorHandle, 1);
	handle->arch = i386_arch_initialize ();
	return handle;
}

static void
child_setup_func (gpointer data)
{
	if (ptrace (PT_TRACE_ME, getpid (), NULL, 0))
		g_error (G_STRLOC ": Can't PT_TRACEME: %s", g_strerror (errno));
	sigprocmask (SIG_UNBLOCK, &mono_debugger_signal_mask, NULL);
}

static void
set_socket_flags (int fd, long flags)
{
	long arg;

	arg = fcntl (fd, F_GETFL);
	fcntl (fd, F_SETFL, arg | flags);

	fcntl (fd, F_SETOWN, getpid ());
}

ServerCommandError
mono_debugger_server_spawn (ServerHandle *handle, const gchar *working_directory,
			    gchar **argv, gchar **envp, gint *child_pid,
			    ChildOutputFunc stdout_handler, ChildOutputFunc stderr_handler,
			    gchar **error)
{	
	InferiorHandle *inferior = handle->inferior;
	int fd[2], open_max, ret, len, i;

	*error = NULL;

	pipe (fd);

	/*
	 * Create two pairs of connected sockets to read the target's stdout and stderr.
	 * We set these sockets to O_ASYNC to receive a SIGIO when output becomes available.
	 *
	 * NOTE: Another way of implementing this is just using a pipe and monitoring it from
	 *       the glib main loop, but I don't want to require having a running glib main
	 *       loop.
	 */
	socketpair (AF_LOCAL, SOCK_STREAM, 0, inferior->output_fd);
	socketpair (AF_LOCAL, SOCK_STREAM, 0, inferior->error_fd);

	inferior->stdout_handler = stdout_handler;
	inferior->stderr_handler = stderr_handler;

	set_socket_flags (inferior->output_fd [0], O_ASYNC | O_NONBLOCK);
	set_socket_flags (inferior->error_fd [0], O_ASYNC | O_NONBLOCK);

	*child_pid = fork ();
	if (*child_pid == 0) {
		gchar *error_message;

		close (0); close (1); close (2);
		dup2 (inferior->output_fd [0], 0);
		dup2 (inferior->output_fd [1], 1);
		dup2 (inferior->error_fd [1], 2);

		open_max = sysconf (_SC_OPEN_MAX);
		for (i = 3; i < open_max; i++)
			fcntl (i, F_SETFD, FD_CLOEXEC);

		child_setup_func (NULL);
		execve (argv [0], argv, envp);

		error_message = g_strdup_printf ("Cannot exec `%s': %s", argv [0], g_strerror (errno));
		len = strlen (error_message) + 1;
		write (fd [1], &len, sizeof (len));
		write (fd [1], error_message, len);
		_exit (1);
	}

	close (fd [1]);
	ret = read (fd [0], &len, sizeof (len));

	if (ret != 0) {
		g_assert (ret == 4);

		*error = g_malloc0 (len);
		read (fd [0], *error, len);
		close (fd [0]);
		close (inferior->output_fd [0]);
		close (inferior->output_fd [1]);
		close (inferior->error_fd [0]);
		close (inferior->error_fd [1]);
		return COMMAND_ERROR_FORK;
	}

	inferior->pid = *child_pid;
	_mono_debugger_server_setup_inferior (inferior);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mono_debugger_server_attach (ServerHandle *handle, int pid)
{
	InferiorHandle *inferior = handle->inferior;

	if (ptrace (PT_ATTACH, pid, NULL, 0)) {
		g_warning (G_STRLOC ": Cannot attach to process %d: %s", pid, g_strerror (errno));
		return COMMAND_ERROR_FORK;
	}

	inferior->pid = pid;
	inferior->is_thread = TRUE;

	_mono_debugger_server_setup_inferior (inferior);

	return COMMAND_ERROR_NONE;
}

static void
process_output (InferiorHandle *inferior, int fd, ChildOutputFunc func)
{
	char buffer [BUFSIZ + 1];
	int count;

	count = read (fd, buffer, BUFSIZ);
	if (count < 0)
		return;

	buffer [count] = 0;
	func (buffer);
}

static void
check_io (InferiorHandle *inferior)
{
	struct pollfd fds [2];
	int ret;

	fds [0].fd = inferior->output_fd [0];
	fds [0].events = POLLIN;
	fds [0].revents = 0;
	fds [1].fd = inferior->error_fd [0];
	fds [1].events = POLLIN;
	fds [1].revents = 0;

	ret = poll (fds, 2, 0);

	if (fds [0].revents == POLLIN)
		process_output (inferior, inferior->output_fd [0], inferior->stdout_handler);
	if (fds [1].revents == POLLIN)
		process_output (inferior, inferior->error_fd [0], inferior->stderr_handler);
}

ServerCommandError
mono_debugger_server_stop (ServerHandle *handle)
{
	kill (handle->inferior->pid, SIGSTOP);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mono_debugger_server_set_signal (ServerHandle *handle, guint32 sig, guint32 send_it)
{
	if (send_it)
		kill (handle->inferior->pid, sig);
	else
		handle->inferior->last_signal = sig;
	return COMMAND_ERROR_NONE;
}

extern void GC_start_blocking (void);
extern void GC_end_blocking (void);

#ifdef __linux__
#include "i386-linux-ptrace.c"
#endif

#ifdef __FreeBSD__
#include "i386-freebsd-ptrace.c"
#endif

