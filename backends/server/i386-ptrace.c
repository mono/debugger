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

typedef struct
{
	INFERIOR_REGS_TYPE *saved_regs;
	INFERIOR_FPREGS_TYPE *saved_fpregs;
	long call_address;
	guint64 callback_argument;
} RuntimeInvokeData;

struct InferiorHandle
{
	int pid;
#ifdef __linux__
	int mem_fd;
#endif
	InferiorInfo *inferior;
	int output_fd [2], error_fd [2];
	ChildOutputFunc stdout_handler, stderr_handler;
	int is_thread;
	int last_signal;
#if 0
	long call_address;
	guint64 callback_argument;
	INFERIOR_REGS_TYPE current_regs;
	INFERIOR_FPREGS_TYPE current_fpregs;
	INFERIOR_REGS_TYPE *saved_regs;
	INFERIOR_FPREGS_TYPE *saved_fpregs;
	GPtrArray *rti_stack;
#endif
	unsigned dr_control, dr_status;
	BreakpointManager *bpm;
};

static void
server_ptrace_finalize (InferiorHandle *handle, ArchInfo *arch)
{
	if (handle->pid) {
		ptrace (PT_KILL, handle->pid, NULL, 0);
		ptrace (PT_DETACH, handle->pid, NULL, 0);
		kill (handle->pid, SIGKILL);
	}
	i386_arch_finalize (arch);
	g_free (handle);
}

static ServerCommandError
server_ptrace_continue (InferiorHandle *handle, ArchInfo *arch)
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
server_ptrace_step (InferiorHandle *handle, ArchInfo *arch)
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
server_ptrace_detach (InferiorHandle *handle, ArchInfo *arch)
{
	if (ptrace (PT_DETACH, handle->pid, NULL, 0)) {
		g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_kill (InferiorHandle *handle, ArchInfo *arch)
{
	if (handle->pid)
		kill (handle->pid, SIGKILL);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_peek_word (InferiorHandle *handle, ArchInfo *arch, guint64 start, int *retval)
{
	return server_ptrace_read_data (handle, arch, start, sizeof (int), retval);
}

static ServerCommandError
server_ptrace_write_data (InferiorHandle *handle, ArchInfo *arch, guint64 start,
			  guint32 size, gconstpointer buffer)
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

	result = server_ptrace_read_data (handle, arch, (guint32) addr, 4, &temp);
	if (result != COMMAND_ERROR_NONE)
		return result;
	memcpy (&temp, ptr, size);

	return server_ptrace_write_data (handle, arch, (guint32) addr, 4, &temp);
}	


static gboolean
dispatch_event (InferiorHandle *handle, ArchInfo *arch, int status, ServerStatusMessageType *type,
		guint64 *arg, guint64 *data1, guint64 *data2)
{
	if (WIFSTOPPED (status)) {
		guint64 callback_arg, retval, retval2;
		ChildStoppedAction action = i386_arch_child_stopped
			(handle, arch, WSTOPSIG (status), &callback_arg, &retval, &retval2);

		switch (action) {
		case STOP_ACTION_SEND_STOPPED:
			*type = MESSAGE_CHILD_STOPPED;
			if (WSTOPSIG (status) == SIGTRAP)
				*arg = 0;
			else
				*arg = WSTOPSIG (status);
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
		handle->pid = 0;
		return TRUE;
	} else if (WIFSIGNALED (status)) {
		*type = MESSAGE_CHILD_SIGNALED;
		*arg = WTERMSIG (status);
		handle->pid = 0;
		return TRUE;
	}

	g_warning (G_STRLOC ": Got unknown waitpid() result: %d", status);
	return FALSE;
}

static void
server_ptrace_wait (InferiorHandle *handle, ArchInfo *arch, ServerStatusMessageType *type,
		    guint64 *arg, guint64 *data1, guint64 *data2)
{
	int status;

	do {
		status = server_do_wait (handle);
		if (status == -1)
			return;

	} while (!dispatch_event (handle, arch, status, type, arg, data1, data2));
}

static InferiorHandle *
server_ptrace_initialize (BreakpointManager *bpm)
{
	InferiorHandle *handle = g_new0 (InferiorHandle, 1);

	handle->inferior = &i386_ptrace_inferior;
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
server_ptrace_spawn (InferiorHandle *handle, ArchInfo *arch, const gchar *working_directory,
		     gchar **argv, gchar **envp, gint *child_pid, ChildOutputFunc stdout_handler,
		     ChildOutputFunc stderr_handler, gchar **error)
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
	server_setup_inferior (handle, arch);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_attach (InferiorHandle *handle, ArchInfo *arch, int pid)
{
	if (ptrace (PT_ATTACH, pid, NULL, 0)) {
		g_warning (G_STRLOC ": Cannot attach to process %d: %s", pid, g_strerror (errno));
		return COMMAND_ERROR_FORK;
	}

	handle->pid = pid;
	handle->is_thread = TRUE;

	server_setup_inferior (handle, arch);

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
server_ptrace_stop (InferiorHandle *handle, ArchInfo *arch)
{
	kill (handle->pid, SIGSTOP);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_set_signal (InferiorHandle *handle, ArchInfo *arch, guint32 sig, guint32 send_it)
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
	i386_arch_initialize,
	server_ptrace_initialize,
	server_ptrace_spawn,
	server_ptrace_attach,
	server_ptrace_detach,
	server_ptrace_finalize,
	server_ptrace_wait,
	i386_arch_get_target_info,
	server_ptrace_continue,
	server_ptrace_step,
	i386_arch_get_pc,
	i386_arch_current_insn_is_bpt,
	server_ptrace_read_data,
	server_ptrace_write_data,
	i386_arch_call_method,
	i386_arch_call_method_1,
	i386_arch_call_method_invoke,
	i386_arch_insert_breakpoint,
	i386_arch_insert_hw_breakpoint,
	i386_arch_remove_breakpoint,
	i386_arch_enable_breakpoint,
	i386_arch_disable_breakpoint,
	i386_arch_get_breakpoints,
	i386_arch_get_registers,
	i386_arch_set_registers,
	i386_arch_get_backtrace,
	i386_arch_get_ret_address,
	server_ptrace_stop,
	server_ptrace_set_signal,
	server_ptrace_kill
};
