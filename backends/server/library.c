#include <server.h>
#include <signal.h>
#include <unistd.h>
#include <sys/poll.h>
#include <sys/select.h>
#include <sys/time.h>
#include <errno.h>

GSource *
mono_debugger_server_get_g_source (ServerHandle *handle)
{
	if (!handle->inferior)
		return NULL;

	return (* handle->info->get_g_source) (handle->inferior);
}

void
mono_debugger_server_wait (ServerHandle *handle)
{
	if (!handle->inferior)
		return;

	(* handle->info->wait) (handle->inferior);
}

ServerCommandError
mono_debugger_server_read_memory (ServerHandle *handle, guint64 start, guint32 size, gpointer *data)
{
	ServerCommandError result;

	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	*data = g_malloc (size);
	result = (* handle->info->read_data) (handle->inferior, start, size, *data);
	if (result != COMMAND_ERROR_NONE) {
		g_free (*data);
		*data = NULL;
	}
	return result;
}

ServerCommandError
mono_debugger_server_write_memory (ServerHandle *handle, gpointer data, guint64 start, guint32 size)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->write_data) (handle->inferior, start, size, data);
}

ServerCommandError
mono_debugger_server_get_target_info (ServerHandle *handle, guint32 *target_int_size,
				      guint32 *target_long_size, guint32 *target_address_size)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->get_target_info) (
		handle->inferior, target_int_size, target_long_size, target_address_size);
}

ServerCommandError
mono_debugger_server_call_method (ServerHandle *handle, guint64 method_address,
				  guint64 method_argument, guint64 callback_argument)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->call_method) (handle->inferior, method_address, method_argument,
					      callback_argument);
}

ServerCommandError
mono_debugger_server_call_method_1 (ServerHandle *handle, guint64 method_address,
				    guint64 method_argument, const gchar *string_argument,
				    guint64 callback_argument)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->call_method_1) (handle->inferior, method_address, method_argument,
						string_argument, callback_argument);
}

ServerCommandError
mono_debugger_server_call_method_invoke (ServerHandle *handle, guint64 invoke_method,
					 guint64 method_argument, guint64 object_argument,
					 guint32 num_params, guint64 *param_data,
					 guint64 callback_argument)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->call_method_invoke) (handle->inferior, invoke_method, method_argument,
						     object_argument, num_params, param_data,
						     callback_argument);
}

ServerCommandError
mono_debugger_server_insert_breakpoint (ServerHandle *handle, guint64 address, guint32 *breakpoint)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->insert_breakpoint) (handle->inferior, address, breakpoint);
}

ServerCommandError
mono_debugger_server_remove_breakpoint (ServerHandle *handle, guint32 breakpoint)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->remove_breakpoint) (handle->inferior, breakpoint);
}

ServerCommandError
mono_debugger_server_enable_breakpoint (ServerHandle *handle, guint32 breakpoint)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->enable_breakpoint) (handle->inferior, breakpoint);
}

ServerCommandError
mono_debugger_server_disable_breakpoint (ServerHandle *handle, guint32 breakpoint)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->disable_breakpoint) (handle->inferior, breakpoint);
}

ServerCommandError
mono_debugger_server_enable_breakpoints (ServerHandle *handle)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->enable_breakpoints) (handle->inferior);
}

ServerCommandError
mono_debugger_server_disable_breakpoints (ServerHandle *handle)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->disable_breakpoints) (handle->inferior);
}

static gboolean initialized = FALSE;

static void
signal_handler (int dummy)
{
	/* Do nothing.  This is just to wake us up. */
}

ServerHandle *
mono_debugger_server_initialize (void)
{
	ServerHandle *handle = g_new0 (ServerHandle, 1);

	if (!initialized) {
		sigset_t mask;

		/* These signals have been blocked by our parent, so we need to unblock them here. */
		sigemptyset (&mask);
		sigaddset (&mask, SIGCHLD);
		sigprocmask (SIG_UNBLOCK, &mask, NULL);

		/* Install our signal handlers. */
		signal (SIGCHLD, signal_handler);

		initialized = TRUE;
	}

	handle->info = &i386_linux_ptrace_inferior;
	return handle;
}

ServerCommandError
mono_debugger_server_spawn (ServerHandle *handle, const gchar *working_directory,
			    gchar **argv, gchar **envp, gboolean search_path,
			    ChildExitedFunc child_exited, ChildMessageFunc child_message,
			    ChildCallbackFunc child_callback, gint *child_pid,
			    gint *standard_input, gint *standard_output, gint *standard_error,
			    GError **error)
{
	if (handle->inferior)
		return COMMAND_ERROR_ALREADY_HAVE_INFERIOR;

	handle->inferior = (* handle->info->spawn) (working_directory, argv, envp, search_path,
						    child_exited, child_message, child_callback,
						    child_pid, standard_input, standard_output,
						    standard_error, error);
	if (!handle->inferior)
		return COMMAND_ERROR_FORK;

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mono_debugger_server_attach (ServerHandle *handle, int pid, ChildExitedFunc child_exited,
			     ChildMessageFunc child_message, ChildCallbackFunc child_callback)
{
	if (handle->inferior)
		return COMMAND_ERROR_ALREADY_HAVE_INFERIOR;

	handle->inferior = (* handle->info->attach) (pid, child_exited, child_message,
						     child_callback);
	if (!handle->inferior)
		return COMMAND_ERROR_FORK;

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mono_debugger_server_get_pc (ServerHandle *handle, guint64 *pc)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->get_pc) (handle->inferior, pc);
}

ServerCommandError
mono_debugger_server_current_insn_is_bpt (ServerHandle *handle, guint32 *is_breakpoint)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->current_insn_is_bpt) (handle->inferior, is_breakpoint);
}

ServerCommandError
mono_debugger_server_step (ServerHandle *handle)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->step) (handle->inferior);
}

ServerCommandError
mono_debugger_server_continue (ServerHandle *handle)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->run) (handle->inferior);
}

ServerCommandError
mono_debugger_server_detach (ServerHandle *handle)
{
	ServerCommandError result;

	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	result = (* handle->info->detach) (handle->inferior);
	handle->inferior = NULL;
	return result;
}

void
mono_debugger_server_finalize (ServerHandle *handle)
{
	if (handle->inferior)
		(* handle->info->finalize) (handle->inferior);
	g_free (handle);
}

ServerCommandError
mono_debugger_server_get_registers (ServerHandle *handle, guint32 count, guint32 *registers,
				    guint64 *values)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->get_registers) (handle->inferior, count, registers, values);
}

ServerCommandError
mono_debugger_server_set_registers (ServerHandle *handle, guint32 count, guint32 *registers,
				    guint64 *values)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->set_registers) (handle->inferior, count, registers, values);
}

ServerCommandError
mono_debugger_server_get_backtrace (ServerHandle *handle, gint32 max_frames, guint64 stop_address,
				    guint32 *count, StackFrame **frames)
{
	*count = 0;
	*frames = NULL;

	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->get_backtrace) (handle->inferior, max_frames,
						stop_address, count, frames);
}

ServerCommandError
mono_debugger_server_get_ret_address (ServerHandle *handle, guint64 *retval)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->get_ret_address) (handle->inferior, retval);
}

ServerCommandError
mono_debugger_server_stop (ServerHandle *handle)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->stop) (handle->inferior);
}

ServerCommandError
mono_debugger_server_set_signal (ServerHandle *handle, guint32 sig)
{
	if (!handle->inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->set_signal) (handle->inferior, sig);
}
