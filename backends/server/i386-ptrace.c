#define _GNU_SOURCE
#include <server.h>
#include <breakpoints.h>
#include <i386-arch.h>
#include <sys/stat.h>
#include <sys/ptrace.h>
#include <sys/socket.h>
#include <sys/wait.h>
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

#include "i386-arch.h"

#ifdef __linux__
#include "i386-linux-ptrace.h"
#endif

#ifdef __FreeBSD__
#include "i386-freebsd-ptrace.h"
#endif

struct InferiorHandle
{
	int pid;
#ifdef __linux__
	int mem_fd;
#endif
	int output_fd [2], error_fd [2];
	ChildOutputFunc stdout_handler, stderr_handler;
	int is_thread;
	int last_signal;
	long call_address;
	guint64 callback_argument;
	INFERIOR_REGS_TYPE current_regs;
	INFERIOR_FPREGS_TYPE current_fpregs;
	INFERIOR_REGS_TYPE *saved_regs;
	INFERIOR_FPREGS_TYPE *saved_fpregs;
	unsigned dr_control, dr_status;
	BreakpointManager *bpm;
};

static void
server_ptrace_finalize (InferiorHandle *handle)
{
	if (handle->pid) {
		ptrace (PT_KILL, handle->pid, NULL, 0);
		ptrace (PT_DETACH, handle->pid, NULL, 0);
		kill (handle->pid, SIGKILL);
	}

	g_free (handle->saved_regs);
	g_free (handle->saved_fpregs);
	g_free (handle);
}

static ServerCommandError
server_ptrace_continue (InferiorHandle *handle)
{
	errno = 0;
	if (ptrace (PT_CONTINUE, handle->pid, (caddr_t) 1, handle->last_signal)) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;

		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_step (InferiorHandle *handle)
{
	errno = 0;
	if (ptrace (PT_STEP, handle->pid, (caddr_t) 1, handle->last_signal)) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;

		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_detach (InferiorHandle *handle)
{
	if (ptrace (PT_DETACH, handle->pid, NULL, 0)) {
		g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_kill (InferiorHandle *handle)
{
	if (handle->pid)
		kill (handle->pid, SIGKILL);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_peek_word (InferiorHandle *handle, guint64 start, int *retval)
{
	return server_ptrace_read_data (handle, start, sizeof (int), retval);

	errno = 0;
	*retval = ptrace (PT_READ_D, handle->pid, (gpointer) ((guint32) start), 0);
	if (errno) {
		g_message (G_STRLOC ": %d - %08Lx - %s", handle->pid, start, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_write_data (InferiorHandle *handle, guint64 start, guint32 size, gconstpointer buffer)
{
	ServerCommandError result;
	const int *ptr = buffer;
	int addr = start;
	char temp [4];

	while (size >= 4) {
		int word = *ptr++;

		errno = 0;
		if (ptrace (PT_WRITE_D, handle->pid, (gpointer) addr, word) != 0) {
			if (errno == ESRCH)
				return COMMAND_ERROR_NOT_STOPPED;
			else if (errno) {
				g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
				return COMMAND_ERROR_UNKNOWN;
			}
		}

		addr += sizeof (int);
		size -= sizeof (int);
	}

	if (!size)
		return COMMAND_ERROR_NONE;

	result = server_ptrace_read_data (handle, (guint32) addr, 4, &temp);
	if (result != COMMAND_ERROR_NONE)
		return result;
	memcpy (&temp, ptr, size);

	return server_ptrace_write_data (handle, (guint32) addr, 4, &temp);
}	

static void
server_ptrace_wait (InferiorHandle *handle, ServerStatusMessageType *type, guint64 *arg,
		    guint64 *data1, guint64 *data2)
{
	int status;

	do {
		status = do_wait (handle);
		if (status == -1)
			return;

	} while (!debugger_arch_i386_dispatch_event (handle, status, type, arg, data1, data2));
}

static InferiorHandle *
server_ptrace_initialize (BreakpointManager *bpm)
{
	InferiorHandle *handle = g_new0 (InferiorHandle, 1);

	handle->bpm = bpm;
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

static ServerCommandError
server_ptrace_spawn (InferiorHandle *handle, const gchar *working_directory, gchar **argv, gchar **envp,
		     gint *child_pid, ChildOutputFunc stdout_handler, ChildOutputFunc stderr_handler,
		     gchar **error)
{
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
	socketpair (AF_LOCAL, SOCK_STREAM, 0, handle->output_fd);
	socketpair (AF_LOCAL, SOCK_STREAM, 0, handle->error_fd);

	handle->stdout_handler = stdout_handler;
	handle->stderr_handler = stderr_handler;

	set_socket_flags (handle->output_fd [0], O_ASYNC | O_NONBLOCK);
	set_socket_flags (handle->error_fd [0], O_ASYNC | O_NONBLOCK);

	*child_pid = fork ();
	if (*child_pid == 0) {
		gchar *error_message;

		close (0); close (1); close (2);
		dup2 (handle->output_fd [0], 0);
		dup2 (handle->output_fd [1], 1);
		dup2 (handle->error_fd [1], 2);

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
		close (handle->output_fd [0]);
		close (handle->output_fd [1]);
		close (handle->error_fd [0]);
		close (handle->error_fd [1]);
		return COMMAND_ERROR_FORK;
	}

	handle->pid = *child_pid;
	setup_inferior (handle);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_attach (InferiorHandle *handle, int pid)
{
	if (ptrace (PT_ATTACH, pid, NULL, 0)) {
		g_warning (G_STRLOC ": Cannot attach to process %d: %s", pid, g_strerror (errno));
		return COMMAND_ERROR_FORK;
	}

	handle->pid = pid;
	handle->is_thread = TRUE;

	setup_inferior (handle);

	return COMMAND_ERROR_NONE;
}

static void
process_output (InferiorHandle *handle, int fd, ChildOutputFunc func)
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
check_io (InferiorHandle *handle)
{
	struct pollfd fds [2];
	int ret;

	fds [0].fd = handle->output_fd [0];
	fds [0].events = POLLIN;
	fds [0].revents = 0;
	fds [1].fd = handle->error_fd [0];
	fds [1].events = POLLIN;
	fds [1].revents = 0;

	ret = poll (fds, 2, 0);

	if (fds [0].revents == POLLIN)
		process_output (handle, handle->output_fd [0], handle->stdout_handler);
	if (fds [1].revents == POLLIN)
		process_output (handle, handle->error_fd [0], handle->stderr_handler);
}

static ServerCommandError
server_ptrace_stop (InferiorHandle *handle)
{
	kill (handle->pid, SIGSTOP);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_set_signal (InferiorHandle *handle, guint32 sig, guint32 send_it)
{
	if (send_it)
		kill (handle->pid, sig);
	else
		handle->last_signal = sig;
	return COMMAND_ERROR_NONE;
}

extern void GC_start_blocking (void);
extern void GC_end_blocking (void);

#include "i386-arch.c"

#ifdef __linux__
#include "i386-linux-ptrace.c"
#endif

#ifdef __FreeBSD__
#include "i386-freebsd-ptrace.c"
#endif

/*
 * Method VTable for this backend.
 */
InferiorInfo i386_ptrace_inferior = {
	server_ptrace_initialize,
	server_ptrace_spawn,
	server_ptrace_attach,
	server_ptrace_detach,
	server_ptrace_finalize,
	server_ptrace_wait,
	server_ptrace_get_target_info,
	server_ptrace_continue,
	server_ptrace_step,
	server_ptrace_get_pc,
	server_ptrace_current_insn_is_bpt,
	server_ptrace_read_data,
	server_ptrace_write_data,
	server_ptrace_call_method,
	server_ptrace_call_method_1,
	server_ptrace_call_method_invoke,
	server_ptrace_insert_breakpoint,
	server_ptrace_insert_hw_breakpoint,
	server_ptrace_remove_breakpoint,
	server_ptrace_enable_breakpoint,
	server_ptrace_disable_breakpoint,
	server_ptrace_get_breakpoints,
	server_ptrace_get_registers,
	server_ptrace_set_registers,
	server_ptrace_get_backtrace,
	server_ptrace_get_ret_address,
	server_ptrace_stop,
	server_ptrace_set_signal,
	server_ptrace_kill
};
