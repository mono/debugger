#define _GNU_SOURCE
#include <config.h>
#include <server.h>
#include <signal.h>
#include <unistd.h>
#if defined(__linux__) || defined(__FreeBSD__)
#include <sys/poll.h>
#include <sys/select.h>
#endif
#include <sys/time.h>
#include <errno.h>
#include <stdio.h>

void
mono_debugger_server_wait (ServerHandle *handle, ServerStatusMessageType *type, guint64 *arg,
			   guint64 *data1, guint64 *data2)
{
	if (!handle->has_inferior)
		return;

	(* handle->info->wait) (handle->inferior, handle->arch, type, arg, data1, data2);
}

ServerCommandError
mono_debugger_server_read_memory (ServerHandle *handle, guint64 start, guint32 size, gpointer *data)
{
	ServerCommandError result;

	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	*data = g_malloc (size);
	result = (* handle->info->read_data) (handle->inferior, handle->arch, start, size, *data);
	if (result != COMMAND_ERROR_NONE) {
		g_free (*data);
		*data = NULL;
	}
	return result;
}

ServerCommandError
mono_debugger_server_write_memory (ServerHandle *handle, gpointer data, guint64 start, guint32 size)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->write_data) (handle->inferior, handle->arch, start, size, data);
}

ServerCommandError
mono_debugger_server_get_target_info (ServerHandle *handle, guint32 *target_int_size,
				      guint32 *target_long_size, guint32 *target_address_size)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->get_target_info) (
		handle->inferior, handle->arch, target_int_size, target_long_size, target_address_size);
}

ServerCommandError
mono_debugger_server_call_method (ServerHandle *handle, guint64 method_address,
				  guint64 method_argument1, guint64 method_argument2,
				  guint64 callback_argument)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->call_method) (handle->inferior, handle->arch, method_address,
					      method_argument1, method_argument2, callback_argument);
}

ServerCommandError
mono_debugger_server_call_method_1 (ServerHandle *handle, guint64 method_address,
				    guint64 method_argument, const gchar *string_argument,
				    guint64 callback_argument)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->call_method_1) (handle->inferior, handle->arch, method_address,
						method_argument, string_argument, callback_argument);
}

ServerCommandError
mono_debugger_server_call_method_invoke (ServerHandle *handle, guint64 invoke_method,
					 guint64 method_argument, guint64 object_argument,
					 guint32 num_params, guint64 *param_data,
					 guint64 callback_argument)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->call_method_invoke) (handle->inferior, handle->arch, invoke_method,
						     method_argument, object_argument, num_params,
						     param_data, callback_argument);
}

ServerCommandError
mono_debugger_server_insert_breakpoint (ServerHandle *handle, guint64 address, guint32 *breakpoint)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->insert_breakpoint) (handle->inferior, handle->arch, address, breakpoint);
}

ServerCommandError
mono_debugger_server_insert_hw_breakpoint (ServerHandle *handle, guint32 idx, guint64 address,
					   guint32 *breakpoint)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->insert_hw_breakpoint) (handle->inferior, handle->arch, idx,
						       address, breakpoint);
}

ServerCommandError
mono_debugger_server_remove_breakpoint (ServerHandle *handle, guint32 breakpoint)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->remove_breakpoint) (handle->inferior, handle->arch, breakpoint);
}

ServerCommandError
mono_debugger_server_enable_breakpoint (ServerHandle *handle, guint32 breakpoint)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->enable_breakpoint) (handle->inferior, handle->arch, breakpoint);
}

ServerCommandError
mono_debugger_server_disable_breakpoint (ServerHandle *handle, guint32 breakpoint)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->disable_breakpoint) (handle->inferior, handle->arch, breakpoint);
}

static gboolean initialized = FALSE;

#if defined(__linux__) || defined(__FreeBSD__)
extern InferiorInfo i386_ptrace_inferior;
#include "i386-ptrace.c"
#endif
#if defined(PLATFORM_WIN32)
extern InferiorInfo i386_win32_inferior;
#include "i386-win32.c"
#endif

sigset_t mono_debugger_signal_mask;

ServerHandle *
mono_debugger_server_initialize (BreakpointManager *breakpoint_manager)
{
	ServerHandle *handle = g_new0 (ServerHandle, 1);

	if (!initialized) {
#if defined(__linux__) || defined(__FreeBSD__)
		/* These signals are only unblocked by sigwait(). */
		sigemptyset (&mono_debugger_signal_mask);
		sigaddset (&mono_debugger_signal_mask, SIGCHLD);
		sigaddset (&mono_debugger_signal_mask, SIGINT);
		sigaddset (&mono_debugger_signal_mask, SIGIO);
		sigprocmask (SIG_BLOCK, &mono_debugger_signal_mask, NULL);
#endif

		initialized = TRUE;
	}

#if defined(__linux__) || defined(__FreeBSD__)
	handle->info = &i386_ptrace_inferior;
	handle->arch = (* handle->info->arch_initialize) ();
	handle->inferior = (* handle->info->initialize) (breakpoint_manager);
#endif
#if defined(PLATFORM_WIN32)
	handle->info = &i386_win32_inferior;
	handle->arch = (* handle->info->arch_initialize) ();
	handle->inferior = (* handle->info->initialize) (breakpoint_manager);
#endif

	return handle;
}

ServerCommandError
mono_debugger_server_spawn (ServerHandle *handle, const gchar *working_directory,
			    gchar **argv, gchar **envp, gint *child_pid,
			    ChildOutputFunc stdout_handler, ChildOutputFunc stderr_handler,
			    gchar **error)
{
	ServerCommandError result;

	if (handle->has_inferior)
		return COMMAND_ERROR_ALREADY_HAVE_INFERIOR;

	result = (* handle->info->spawn) (handle->inferior, handle->arch, working_directory, argv, envp,
					  child_pid, stdout_handler, stderr_handler, error);
	if (result == COMMAND_ERROR_NONE)
		handle->has_inferior = TRUE;

	return result;
}

ServerCommandError
mono_debugger_server_attach (ServerHandle *handle, int pid)
{
	ServerCommandError result;

	if (handle->has_inferior)
		return COMMAND_ERROR_ALREADY_HAVE_INFERIOR;

	result = (* handle->info->attach) (handle->inferior, handle->arch, pid);
	if (result == COMMAND_ERROR_NONE)
		handle->has_inferior = TRUE;

	return result;
}

ServerCommandError
mono_debugger_server_get_pc (ServerHandle *handle, guint64 *pc)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->get_pc) (handle->inferior, handle->arch, pc);
}

ServerCommandError
mono_debugger_server_current_insn_is_bpt (ServerHandle *handle, guint32 *is_breakpoint)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->current_insn_is_bpt) (handle->inferior, handle->arch, is_breakpoint);
}

ServerCommandError
mono_debugger_server_step (ServerHandle *handle)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->step) (handle->inferior, handle->arch);
}

ServerCommandError
mono_debugger_server_continue (ServerHandle *handle)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->run) (handle->inferior, handle->arch);
}

ServerCommandError
mono_debugger_server_detach (ServerHandle *handle)
{
	ServerCommandError result;

	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	result = (* handle->info->detach) (handle->inferior, handle->arch);
	handle->inferior = NULL;
	return result;
}

void
mono_debugger_server_finalize (ServerHandle *handle)
{
	if (handle->inferior)
		(* handle->info->finalize) (handle->inferior, handle->arch);
	g_free (handle);
}

ServerCommandError
mono_debugger_server_get_registers (ServerHandle *handle, guint32 count, guint32 *registers,
				    guint64 *values)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->get_registers) (handle->inferior, handle->arch, count, registers, values);
}

ServerCommandError
mono_debugger_server_set_registers (ServerHandle *handle, guint32 count, guint32 *registers,
				    guint64 *values)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->set_registers) (handle->inferior, handle->arch, count, registers, values);
}

ServerCommandError
mono_debugger_server_get_backtrace (ServerHandle *handle, gint32 max_frames, guint64 stop_address,
				    guint32 *count, StackFrame **frames)
{
	*count = 0;
	*frames = NULL;

	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->get_backtrace) (handle->inferior, handle->arch, max_frames,
						stop_address, count, frames);
}

ServerCommandError
mono_debugger_server_get_ret_address (ServerHandle *handle, guint64 *retval)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->get_ret_address) (handle->inferior, handle->arch, retval);
}

ServerCommandError
mono_debugger_server_stop (ServerHandle *handle)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->stop) (handle->inferior, handle->arch);
}

ServerCommandError
mono_debugger_server_set_signal (ServerHandle *handle, guint32 sig, guint32 send_it)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->set_signal) (handle->inferior, handle->arch, sig, send_it);
}

ServerCommandError
mono_debugger_server_kill (ServerHandle *handle)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->kill) (handle->inferior, handle->arch);
}

ServerCommandError
mono_debugger_server_get_signal_info (ServerHandle *handle, SignalInfo *sinfo)
{
	if (!handle->has_inferior)
		return COMMAND_ERROR_NO_INFERIOR;

	return (* handle->info->get_signal_info) (handle->inferior, handle->arch, sinfo);

}
