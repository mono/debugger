#include <server.h>
#include <sys/types.h>
#include <sys/ptrace.h>
#include <sys/wait.h>
#include <unistd.h>
#include <fcntl.h>
#include <errno.h>
#include <string.h>
#include <breakpoints.h>

struct InferiorHandle {
	int pid;
};

static ServerHandle *
powerpc_initialize (BreakpointManager *bpm)
{
	ServerHandle *handle = g_new0 (ServerHandle, 1);

	handle->bpm = bpm;
	handle->inferior = g_new0 (InferiorHandle, 1);
	return handle;
}

static int
do_wait (int pid, guint32 *status)
{
	int ret;

	ret = waitpid (pid, status, WUNTRACED);
	if (ret < 0) {
		if (errno == EINTR)
			return 0;
		g_warning (G_STRLOC ": Can't waitpid for %d: %s", pid, g_strerror (errno));
		return -1;
	}

	return ret;
}

static int first_status = 0;
static int first_ret = 0;

static void
_powerpc_setup_inferior (ServerHandle *handle, gboolean is_main)
{
	int status, ret;

	do {
		ret = do_wait (handle->inferior->pid, &status);
	} while (ret == 0);

	if (is_main) {
		g_assert (ret == handle->inferior->pid);
		first_status = status;
		first_ret = ret;
	}
}

static void
child_setup_func (gpointer data)
{
	if (ptrace (PT_TRACE_ME, getpid (), NULL, 0))
		g_error (G_STRLOC ": Can't PT_TRACEME: %s", g_strerror (errno));
}

static ServerCommandError
powerpc_spawn (ServerHandle *handle, const gchar *working_directory,
	       const gchar **argv, const gchar **envp, gint *child_pid,
	       ChildOutputFunc stdout_handler, ChildOutputFunc stderr_handler,
	       gchar **error)
{
	InferiorHandle *inferior = handle->inferior;
	int fd[2], open_max, ret, len, i;

	*error = NULL;

	pipe (fd);

	*child_pid = fork ();
	if (*child_pid == 0) {
		gchar *error_message;

		open_max = sysconf (_SC_OPEN_MAX);
		for (i = 3; i < open_max; i++)
			fcntl (i, F_SETFD, FD_CLOEXEC);

		setsid ();

		child_setup_func (NULL);
		execve (argv [0], (char **) argv, (char **) envp);

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
		return COMMAND_ERROR_FORK;
	}

	inferior->pid = *child_pid;
	_powerpc_setup_inferior (handle, TRUE);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
powerpc_get_target_info (guint32 *target_int_size, guint32 *target_long_size,
			 guint32 *target_address_size)
{
	*target_int_size = sizeof (guint32);
	*target_long_size = sizeof (guint64);
	*target_address_size = sizeof (void *);

	return COMMAND_ERROR_NONE;
}

InferiorVTable powerpc_darwin_inferior = {
	powerpc_initialize,
	powerpc_spawn,
	NULL, // powerpc_attach,
	NULL, // powerpc_detach,
	NULL, // powerpc_finalize,
	NULL, // powerpc_global_wait,
	NULL, // powerpc_stop_and_wait,
	NULL, // powerpc_dispatch_event,
	powerpc_get_target_info,
	NULL, // powerpc_continue,
	NULL, // powerpc_step,
	NULL, // powerpc_get_pc,
	NULL, // powerpc_current_insn_is_bpt,
	NULL, // powerpc_peek_word,
	NULL, // powerpc_read_memory,
	NULL, // powerpc_write_memory,
	NULL, // powerpc_call_method,
	NULL, // powerpc_call_method_1,
	NULL, // powerpc_call_method_invoke,
	NULL, // powerpc_insert_breakpoint,
	NULL, // powerpc_insert_hw_breakpoint,
	NULL, // powerpc_remove_breakpoint,
	NULL, // powerpc_enable_breakpoint,
	NULL, // powerpc_disable_breakpoint,
	NULL, // powerpc_get_breakpoints,
	NULL, // powerpc_get_registers,
	NULL, // powerpc_set_registers,
	NULL, // powerpc_get_backtrace,
	NULL, // powerpc_get_ret_address,
	NULL, // powerpc_stop,
	NULL, // powerpc_global_stop,
	NULL, // powerpc_set_signal,
	NULL, // powerpc_kill,
	NULL // powerpc_get_signal_info
};
