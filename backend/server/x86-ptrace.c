#ifndef _GNU_SOURCE
#define _GNU_SOURCE
#endif

#include <server.h>
#include <breakpoints.h>
#include <stdio.h>
#include <stdlib.h>
#include <pthread.h>
#include <semaphore.h>
#include <sys/stat.h>
#include <sys/ptrace.h>
#include <sys/socket.h>
#include <sys/wait.h>
#include <sys/poll.h>
#include <sys/select.h>
#include <sys/resource.h>
#include <signal.h>
#include <unistd.h>
#include <sys/syscall.h>
#include <string.h>
#include <fcntl.h>
#include <errno.h>

#include <mono/metadata/appdomain.h>
#include <mono/metadata/object.h>

/*
 * NOTE:  The manpage is wrong about the POKE_* commands - the last argument
 *        is the data (a word) to be written, not a pointer to it.
 *
 * In general, the ptrace(2) manpage is very bad, you should really read
 * kernel/ptrace.c and arch/i386/kernel/ptrace.c in the Linux source code
 * to get a better understanding for this stuff.
 */

#ifdef __linux__
#include "x86-linux-ptrace.h"
#endif

#ifdef __FreeBSD__
#include "x86-freebsd-ptrace.h"
#endif

#include "x86-arch.h"

static guint32 io_thread (gpointer data);

struct InferiorHandle
{
	guint32 pid;
#ifdef __linux__
	int mem_fd;
#endif
	int last_signal;
	int output_fd [2], error_fd [2];
	int is_thread, is_initialized;
};

typedef struct
{
	int output_fd, error_fd;
	ChildOutputFunc stdout_handler, stderr_handler;
} IOThreadData;

MonoRuntimeInfo *
mono_debugger_server_initialize_mono_runtime (guint32 address_size,
					      guint64 notification_address,
					      guint64 executable_code_buffer,
					      guint32 executable_code_buffer_size,
					      guint64 breakpoint_info_area,
					      guint64 breakpoint_table,
					      guint32 breakpoint_table_size)
{
	MonoRuntimeInfo *runtime = g_new0 (MonoRuntimeInfo, 1);

	runtime->address_size = address_size;
	runtime->notification_address = notification_address;
	runtime->executable_code_buffer = executable_code_buffer;
	runtime->executable_code_buffer_size = executable_code_buffer_size;
	runtime->executable_code_chunk_size = EXECUTABLE_CODE_CHUNK_SIZE;
	runtime->executable_code_total_chunks = executable_code_buffer_size / EXECUTABLE_CODE_CHUNK_SIZE;

	runtime->breakpoint_info_area = breakpoint_info_area;
	runtime->breakpoint_table = breakpoint_table;
	runtime->breakpoint_table_size = breakpoint_table_size;

	runtime->breakpoint_table_bitfield = g_malloc0 (breakpoint_table_size);
	runtime->executable_code_bitfield = g_malloc0 (runtime->executable_code_total_chunks);

	return runtime;
}

void
mono_debugger_server_finalize_mono_runtime (MonoRuntimeInfo *runtime)
{
	runtime->executable_code_buffer = 0;
}

static void
server_ptrace_finalize (ServerHandle *handle)
{
	x86_arch_finalize (handle->arch);
	g_free (handle->inferior);
	g_free (handle);
}

static ServerCommandError
server_ptrace_continue (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;

	errno = 0;
	if (ptrace (PT_CONTINUE, inferior->pid, (caddr_t) 1, inferior->last_signal)) {
		return _server_ptrace_check_errno (inferior);
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_step (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;

	errno = 0;
	if (ptrace (PT_STEP, inferior->pid, (caddr_t) 1, inferior->last_signal))
		return _server_ptrace_check_errno (inferior);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_detach (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;

	if (ptrace (PT_DETACH, inferior->pid, NULL, 0)) {
		g_message (G_STRLOC ": %d - %s", inferior->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_kill (ServerHandle *handle)
{
	if (ptrace (PTRACE_KILL, handle->inferior->pid, NULL, 0))
		return COMMAND_ERROR_UNKNOWN_ERROR;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_peek_word (ServerHandle *handle, guint64 start, guint64 *retval)
{
	return server_ptrace_read_memory (handle, start, sizeof (gsize), retval);
}

static ServerCommandError
server_ptrace_write_memory (ServerHandle *handle, guint64 start,
			    guint32 size, gconstpointer buffer)
{
	InferiorHandle *inferior = handle->inferior;
	ServerCommandError result;
	const long *ptr = buffer;
	guint64 addr = start;
	char temp [8];

	while (size >= sizeof (long)) {
		long word = *ptr++;

		errno = 0;
		if (ptrace (PT_WRITE_D, inferior->pid, GSIZE_TO_POINTER (addr), word) != 0)
			return _server_ptrace_check_errno (inferior);

		addr += sizeof (long);
		size -= sizeof (long);
	}

	if (!size)
		return COMMAND_ERROR_NONE;

	result = _server_ptrace_read_memory (handle, addr, sizeof (long), &temp);
	if (result != COMMAND_ERROR_NONE)
		return result;

	memcpy (&temp, ptr, size);

	return server_ptrace_write_memory (handle, addr, sizeof (long), &temp);
}

static ServerCommandError
server_ptrace_poke_word (ServerHandle *handle, guint64 addr, gsize value)
{
	errno = 0;
	if (ptrace (PT_WRITE_D, handle->inferior->pid, GSIZE_TO_POINTER (addr), value) != 0)
		return _server_ptrace_check_errno (handle->inferior);

	return COMMAND_ERROR_NONE;
}

static ServerStatusMessageType
server_ptrace_dispatch_event (ServerHandle *handle, guint32 status, guint64 *arg,
			      guint64 *data1, guint64 *data2, guint32 *opt_data_size,
			      gpointer *opt_data)
{
	if (status >> 16) {
		switch (status >> 16) {
		case PTRACE_EVENT_CLONE: {
			int new_pid;

			if (ptrace (PTRACE_GETEVENTMSG, handle->inferior->pid, 0, &new_pid)) {
				g_warning (G_STRLOC ": %d - %s", handle->inferior->pid,
					   g_strerror (errno));
				return FALSE;
			}

			*arg = new_pid;
			return MESSAGE_CHILD_CREATED_THREAD;
		}

		case PTRACE_EVENT_FORK: {
			int new_pid;

			if (ptrace (PTRACE_GETEVENTMSG, handle->inferior->pid, 0, &new_pid)) {
				g_warning (G_STRLOC ": %d - %s", handle->inferior->pid,
					   g_strerror (errno));
				return FALSE;
			}

			*arg = new_pid;
			return MESSAGE_CHILD_FORKED;
		}

		case PTRACE_EVENT_EXEC:
			return MESSAGE_CHILD_EXECD;

		case PTRACE_EVENT_EXIT: {
			int exitcode;

			if (ptrace (PTRACE_GETEVENTMSG, handle->inferior->pid, 0, &exitcode)) {
				g_warning (G_STRLOC ": %d - %s", handle->inferior->pid,
					   g_strerror (errno));
				return FALSE;
			}

			*arg = 0;
			return MESSAGE_CHILD_CALLED_EXIT;
		}

		default:
			g_warning (G_STRLOC ": Received unknown wait result %x on child %d",
				   status, handle->inferior->pid);
			return MESSAGE_UNKNOWN_ERROR;
		}
	}

	if (WIFSTOPPED (status)) {
		guint64 callback_arg, retval, retval2;
		ChildStoppedAction action;
		int stopsig;

		stopsig = WSTOPSIG (status);

		if (!handle->inferior->is_initialized) {
			x86_arch_remove_hardware_breakpoints (handle);
			handle->inferior->is_initialized = TRUE;
			if (stopsig == SIGSTOP)
				stopsig = 0;
		}

		action = x86_arch_child_stopped (
			handle, stopsig, &callback_arg, &retval, &retval2, opt_data_size, opt_data);

		if (action != STOP_ACTION_STOPPED)
			handle->inferior->last_signal = 0;

		switch (action) {
		case STOP_ACTION_STOPPED:
			if (stopsig == SIGTRAP) {
				handle->inferior->last_signal = 0;
				*arg = 0;
			} else {
				handle->inferior->last_signal = stopsig;
				*arg = stopsig;
			}
			return MESSAGE_CHILD_STOPPED;

		case STOP_ACTION_INTERRUPTED:
			*arg = 0;
			return MESSAGE_CHILD_INTERRUPTED;

		case STOP_ACTION_BREAKPOINT_HIT:
			*arg = (int) retval;
			return MESSAGE_CHILD_HIT_BREAKPOINT;

		case STOP_ACTION_CALLBACK:
			*arg = callback_arg;
			*data1 = retval;
			*data2 = retval2;
			return MESSAGE_CHILD_CALLBACK;

		case STOP_ACTION_CALLBACK_COMPLETED:
			*arg = callback_arg;
			*data1 = retval;
			*data2 = retval2;
			return MESSAGE_CHILD_CALLBACK_COMPLETED;

		case STOP_ACTION_NOTIFICATION:
			*arg = callback_arg;
			*data1 = retval;
			*data2 = retval2;
			return MESSAGE_CHILD_NOTIFICATION;
		}

		g_assert_not_reached ();
	} else if (WIFEXITED (status)) {
		*arg = WEXITSTATUS (status);
		return MESSAGE_CHILD_EXITED;
	} else if (WIFSIGNALED (status)) {
		if ((WTERMSIG (status) == SIGTRAP) || (WTERMSIG (status) == SIGKILL)) {
			*arg = 0;
			return MESSAGE_CHILD_EXITED;
		} else {
			*arg = WTERMSIG (status);
			return MESSAGE_CHILD_SIGNALED;
		}
	}

	g_warning (G_STRLOC ": Got unknown waitpid() result: %x", status);
	return MESSAGE_UNKNOWN_ERROR;
}

static ServerHandle *
server_ptrace_create_inferior (BreakpointManager *bpm)
{
	ServerHandle *handle = g_new0 (ServerHandle, 1);

	handle->bpm = bpm;
	handle->inferior = g_new0 (InferiorHandle, 1);
	handle->arch = x86_arch_initialize ();

	return handle;
}

static void
child_setup_func (InferiorHandle *inferior)
{
	if (ptrace (PT_TRACE_ME, getpid (), NULL, 0))
		g_error (G_STRLOC ": Can't PT_TRACEME: %s", g_strerror (errno));

	dup2 (inferior->output_fd[1], 1);
	dup2 (inferior->error_fd[1], 2);
}

static ServerCommandError
server_ptrace_spawn (ServerHandle *handle, const gchar *working_directory,
		     const gchar **argv, const gchar **envp, gint *child_pid,
		     ChildOutputFunc stdout_handler, ChildOutputFunc stderr_handler,
		     gchar **error)
{	
	InferiorHandle *inferior = handle->inferior;
	IOThreadData *io_data = g_new0 (IOThreadData, 1);
	int fd[2], open_max, ret, len, i;
	ServerCommandError result;

	*error = NULL;

	pipe (fd);

	pipe (inferior->output_fd);
	pipe (inferior->error_fd);

	io_data->output_fd = inferior->output_fd[0];
	io_data->error_fd = inferior->error_fd[0];

	io_data->stdout_handler = stdout_handler;
	io_data->stderr_handler = stderr_handler;

	*child_pid = fork ();
	if (*child_pid == 0) {
		gchar *error_message;
		struct rlimit core_limit;

		open_max = sysconf (_SC_OPEN_MAX);
		for (i = 3; i < open_max; i++)
			fcntl (i, F_SETFD, FD_CLOEXEC);

		setsid ();

		getrlimit (RLIMIT_CORE, &core_limit);
		core_limit.rlim_cur = 0;
		setrlimit (RLIMIT_CORE, &core_limit);

		child_setup_func (inferior);
		execve (argv [0], (char **) argv, (char **) envp);

		error_message = g_strdup_printf ("Cannot exec `%s': %s", argv [0], g_strerror (errno));
		len = strlen (error_message) + 1;
		write (fd [1], &len, sizeof (len));
		write (fd [1], error_message, len);
		_exit (1);
	}

	close (inferior->output_fd[1]);
	close (inferior->error_fd[1]);
	close (fd [1]);

	ret = read (fd [0], &len, sizeof (len));

	if (ret != 0) {
		g_assert (ret == 4);

		*error = g_malloc0 (len);
		read (fd [0], *error, len);
		close (fd [0]);
		close (inferior->output_fd[0]);
		close (inferior->error_fd[0]);
		return COMMAND_ERROR_CANNOT_START_TARGET;
	}

	inferior->pid = *child_pid;

	result = _server_ptrace_setup_inferior (handle);
	if (result != COMMAND_ERROR_NONE)
		return result;

	mono_thread_create (mono_domain_get (), io_thread, io_data);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_initialize_thread (ServerHandle *handle, guint32 pid)
{
	InferiorHandle *inferior = handle->inferior;

	inferior->pid = pid;
	inferior->is_thread = TRUE;

	return _server_ptrace_setup_inferior (handle);
}

static ServerCommandError
server_ptrace_attach (ServerHandle *handle, guint32 pid)
{
	InferiorHandle *inferior = handle->inferior;

	if (ptrace (PT_ATTACH, pid, NULL, 0) != 0) {
		g_warning (G_STRLOC ": Can't attach to %d - %s", pid,
			   g_strerror (errno));
		return COMMAND_ERROR_CANNOT_START_TARGET;
	}

	inferior->pid = pid;
	inferior->is_thread = TRUE;

	return _server_ptrace_setup_inferior (handle);
}

static void
process_output (int fd, ChildOutputFunc func)
{
	char buffer [BUFSIZ + 1];
	int count;

	count = read (fd, buffer, BUFSIZ);
	if (count < 0)
		return;

	buffer [count] = 0;
	func (buffer);
}

static guint32
io_thread (gpointer data)
{
	IOThreadData *io_data = (IOThreadData*)data;
	struct pollfd fds [2];
	int ret;

	fds [0].fd = io_data->output_fd;
	fds [0].events = POLLIN | POLLHUP | POLLERR;
	fds [0].revents = 0;
	fds [1].fd = io_data->error_fd;
	fds [1].events = POLLIN | POLLHUP | POLLERR;
	fds [1].revents = 0;

	while (1) {
		ret = poll (fds, 2, -1);

		if ((ret < 0) && (errno != EINTR))
			break;

		if (fds [0].revents & POLLIN)
			process_output (io_data->output_fd, io_data->stdout_handler);
		if (fds [1].revents & POLLIN)
			process_output (io_data->error_fd, io_data->stderr_handler);

		if ((fds [0].revents & (POLLHUP | POLLERR))
		    || (fds [1].revents & (POLLHUP | POLLERR)))
			break;
	}

	g_free (io_data);
	return 0;
}

static ServerCommandError
server_ptrace_set_signal (ServerHandle *handle, guint32 sig, guint32 send_it)
{
	if (send_it)
		kill (handle->inferior->pid, sig);
	else
		handle->inferior->last_signal = sig;
	return COMMAND_ERROR_NONE;
}

static void
server_ptrace_set_runtime_info (ServerHandle *handle, MonoRuntimeInfo *mono_runtime)
{
	handle->mono_runtime = mono_runtime;
}

extern void GC_start_blocking (void);
extern void GC_end_blocking (void);

#ifdef __linux__
#include "x86-linux-ptrace.c"
#endif

#ifdef __FreeBSD__
#include "x86-freebsd-ptrace.c"
#endif

#if defined(__i386__)
#include "i386-arch.c"
#elif defined(__x86_64__)
#include "x86_64-arch.c"
#else
#error "Unknown architecture"
#endif

InferiorVTable i386_ptrace_inferior = {
	server_ptrace_global_init,
	server_ptrace_create_inferior,
	server_ptrace_initialize_process,
	server_ptrace_initialize_thread,
	server_ptrace_set_runtime_info,
	server_ptrace_spawn,
	server_ptrace_attach,
	server_ptrace_detach,
	server_ptrace_finalize,
	server_ptrace_global_wait,
	server_ptrace_stop_and_wait,
	server_ptrace_dispatch_event,
	server_ptrace_get_target_info,
	server_ptrace_continue,
	server_ptrace_step,
	server_ptrace_get_frame,
	server_ptrace_current_insn_is_bpt,
	server_ptrace_peek_word,
	server_ptrace_read_memory,
	server_ptrace_write_memory,
	server_ptrace_call_method,
	server_ptrace_call_method_1,
	server_ptrace_call_method_2,
	server_ptrace_call_method_invoke,
	server_ptrace_execute_instruction,
	server_ptrace_mark_rti_frame,
	server_ptrace_abort_invoke,
	server_ptrace_insert_breakpoint,
	server_ptrace_insert_hw_breakpoint,
	server_ptrace_remove_breakpoint,
	server_ptrace_enable_breakpoint,
	server_ptrace_disable_breakpoint,
	server_ptrace_get_breakpoints,
	server_ptrace_get_registers,
	server_ptrace_set_registers,
	server_ptrace_stop,
	server_ptrace_set_signal,
	server_ptrace_kill,
	server_ptrace_get_signal_info,
	server_ptrace_get_threads,
	server_ptrace_get_application,
	server_ptrace_init_after_fork,
	server_ptrace_push_registers,
	server_ptrace_pop_registers,
	server_ptrace_get_callback_frame
};
