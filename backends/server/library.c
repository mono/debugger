#define _GNU_SOURCE
#include <config.h>
#include <server.h>
#include <signal.h>
#include <unistd.h>
#if defined(__linux__) || defined(__FreeBSD__)
#include <sys/poll.h>
#include <sys/select.h>
#endif
#include <pthread.h>
#include <semaphore.h>
#include <sys/time.h>
#include <errno.h>
#include <stdio.h>

#if defined(__POWERPC__)
extern InferiorVTable powerpc_darwin_inferior;
InferiorVTable *global_vtable = &powerpc_darwin_inferior;
#else
extern InferiorVTable i386_ptrace_inferior;
static InferiorVTable *global_vtable = &i386_ptrace_inferior;
#endif
#if MARTIN_HACKS
extern InferiorVTable remote_client_inferior;
#endif

ServerHandle *
mono_debugger_server_initialize (BreakpointManager *breakpoint_manager)
{
#if MARTIN_HACKS
	const gchar *remote_var = g_getenv ("MONO_DEBUGGER_REMOTE");

	if (remote_var)
		global_vtable = &remote_client_inferior;

	return global_vtable->initialize (breakpoint_manager);
#else
	return global_vtable->initialize (breakpoint_manager);
#endif
}

ServerCommandError
mono_debugger_server_spawn (ServerHandle *handle, const gchar *working_directory,
			    const gchar **argv, const gchar **envp, gint *child_pid,
			    ChildOutputFunc stdout_handler, ChildOutputFunc stderr_handler,
			    gchar **error)
{
	if (!global_vtable->spawn)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->spawn) (handle, working_directory, argv, envp,
					 child_pid, stdout_handler, stderr_handler, error);
}

ServerCommandError
mono_debugger_server_attach (ServerHandle *handle, guint32 pid, guint32 *tid)
{
	if (!global_vtable->attach)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->attach) (handle, pid, tid);
}

void
mono_debugger_server_finalize (ServerHandle *handle)
{
	(* global_vtable->finalize) (handle);
}

void
mono_debugger_server_global_init (void)
{
	(* global_vtable->global_init) ();
}

guint32
mono_debugger_server_global_wait (guint32 *status)
{
	return (* global_vtable->global_wait) (status);
}

ServerStatusMessageType
mono_debugger_server_dispatch_event (ServerHandle *handle, guint32 status, guint64 *arg,
				     guint64 *data1, guint64 *data2)
{
	return (*global_vtable->dispatch_event) (handle, status, arg, data1, data2);
}

ServerCommandError
mono_debugger_server_get_target_info (guint32 *target_int_size, guint32 *target_long_size,
				      guint32 *target_address_size, guint32 *is_bigendian)
{
	if (!global_vtable->get_target_info)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->get_target_info) (
		target_int_size, target_long_size, target_address_size, is_bigendian);
}

ServerCommandError
mono_debugger_server_get_pc (ServerHandle *handle, guint64 *pc)
{
	if (!global_vtable->get_pc)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->get_pc) (handle, pc);
}

ServerCommandError
mono_debugger_server_current_insn_is_bpt (ServerHandle *handle, guint32 *is_breakpoint)
{
	if (!global_vtable->current_insn_is_bpt)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->current_insn_is_bpt) (handle, is_breakpoint);
}

ServerCommandError
mono_debugger_server_step (ServerHandle *handle)
{
	if (!global_vtable->step)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->step) (handle);
}

ServerCommandError
mono_debugger_server_continue (ServerHandle *handle)
{
	if (!global_vtable->run)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->run) (handle);
}

ServerCommandError
mono_debugger_server_detach (ServerHandle *handle)
{
	if (!global_vtable->detach)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->detach) (handle);
}

ServerCommandError
mono_debugger_server_peek_word (ServerHandle *handle, guint64 start, guint32 *word)
{
	if (!global_vtable->peek_word)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->peek_word) (handle, start, word);
}

ServerCommandError
mono_debugger_server_read_memory (ServerHandle *handle, guint64 start, guint32 size, gpointer data)
{
	if (!global_vtable->read_memory)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->read_memory) (handle, start, size, data);
}

ServerCommandError
mono_debugger_server_write_memory (ServerHandle *handle, guint64 start, guint32 size, gconstpointer data)
{
	if (!global_vtable->write_memory)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->write_memory) (handle, start, size, data);
}

ServerCommandError
mono_debugger_server_call_method (ServerHandle *handle, guint64 method_address,
				  guint64 method_argument1, guint64 method_argument2,
				  guint64 callback_argument)
{
	if (!global_vtable->call_method)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->call_method) (
		handle, method_address, method_argument1, method_argument2,
		callback_argument);
}

ServerCommandError
mono_debugger_server_call_method_1 (ServerHandle *handle, guint64 method_address,
				    guint64 method_argument, const gchar *string_argument,
				    guint64 callback_argument)
{
	if (!global_vtable->call_method_1)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->call_method_1) (
		handle, method_address, method_argument, string_argument, callback_argument);
}

ServerCommandError
mono_debugger_server_call_method_invoke (ServerHandle *handle, guint64 invoke_method,
					 guint64 method_argument, guint64 object_argument,
					 guint32 num_params, guint64 *param_data,
					 guint64 callback_argument, gboolean debug)
{
	if (!global_vtable->call_method_invoke)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->call_method_invoke) (
		handle, invoke_method, method_argument, object_argument, num_params,
		param_data, callback_argument, debug);
}

ServerCommandError
mono_debugger_server_insert_breakpoint (ServerHandle *handle, guint64 address, guint32 *breakpoint)
{
	if (!global_vtable->insert_breakpoint)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->insert_breakpoint) (handle, address, breakpoint);
}

ServerCommandError
mono_debugger_server_insert_hw_breakpoint (ServerHandle *handle, guint32 idx, guint64 address,
					   guint32 *breakpoint)
{
	if (!global_vtable->insert_hw_breakpoint)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->insert_hw_breakpoint) (
		handle, idx, address, breakpoint);
}

ServerCommandError
mono_debugger_server_remove_breakpoint (ServerHandle *handle, guint32 breakpoint)
{
	if (!global_vtable->remove_breakpoint)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->remove_breakpoint) (handle, breakpoint);
}

ServerCommandError
mono_debugger_server_enable_breakpoint (ServerHandle *handle, guint32 breakpoint)
{
	if (!global_vtable->enable_breakpoint)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->enable_breakpoint) (handle, breakpoint);
}

ServerCommandError
mono_debugger_server_disable_breakpoint (ServerHandle *handle, guint32 breakpoint)
{
	if (!global_vtable->disable_breakpoint)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->disable_breakpoint) (handle, breakpoint);
}

ServerCommandError
mono_debugger_server_get_registers (ServerHandle *handle, guint32 count, guint32 *registers,
				    guint64 *values)
{
	if (!global_vtable->get_registers)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->get_registers) (handle, count, registers, values);
}

ServerCommandError
mono_debugger_server_set_registers (ServerHandle *handle, guint32 count, guint32 *registers,
				    guint64 *values)
{
	if (!global_vtable->set_registers)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->set_registers) (handle, count, registers, values);
}

ServerCommandError
mono_debugger_server_get_backtrace (ServerHandle *handle, gint32 max_frames, guint64 stop_address,
				    guint32 *count, StackFrame **frames)
{
	if (!global_vtable->get_backtrace)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->get_backtrace) (
		handle, max_frames, stop_address, count, frames);
}

ServerCommandError
mono_debugger_server_get_ret_address (ServerHandle *handle, guint64 *retval)
{
	if (!global_vtable->get_ret_address)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->get_ret_address) (handle, retval);
}

ServerCommandError
mono_debugger_server_stop (ServerHandle *handle)
{
	if (!global_vtable->stop)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->stop) (handle);
}

void
mono_debugger_server_global_stop (void)
{
	(* global_vtable->global_stop) ();
}

ServerCommandError
mono_debugger_server_stop_and_wait (ServerHandle *handle, guint32 *status)
{
	if (!global_vtable->stop_and_wait)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->stop_and_wait) (handle, status);
}

ServerCommandError
mono_debugger_server_set_signal (ServerHandle *handle, guint32 sig, guint32 send_it)
{
	if (!global_vtable->set_signal)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->set_signal) (handle, sig, send_it);
}

ServerCommandError
mono_debugger_server_kill (ServerHandle *handle)
{
	return (* global_vtable->kill) (handle);
}

ServerCommandError
mono_debugger_server_get_signal_info (ServerHandle *handle, SignalInfo *sinfo)
{
	if (!global_vtable->get_signal_info)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->get_signal_info) (handle, sinfo);

}

void
mono_debugger_server_set_notification (guint64 notification)
{
	if (global_vtable->set_notification)
		return (* global_vtable->set_notification) (notification);
}

static gboolean initialized = FALSE;
static sem_t manager_semaphore;
static int pending_sigint = 0;

static void
sigint_signal_handler (int _dummy)
{
	pending_sigint++;
	sem_post (&manager_semaphore);
}

static void
thread_abort_signal_handler (int _dummy)
{
	pthread_exit (NULL);
}

void
mono_debugger_server_static_init (void)
{
	struct sigaction sa;
	int thread_abort_sig;

	if (initialized)
		return;

	/* catch SIGINT */
	sa.sa_handler = sigint_signal_handler;
	sigemptyset (&sa.sa_mask);
	sa.sa_flags = 0;
	g_assert (sigaction (SIGINT, &sa, NULL) != -1);

#if !defined(__POWERPC__)
	thread_abort_sig = mono_thread_get_abort_signal ();
	if (thread_abort_sig > 0) {
		/* catch SIGINT */
		sa.sa_handler = thread_abort_signal_handler;
		sigemptyset (&sa.sa_mask);
		sa.sa_flags = 0;
		g_assert (sigaction (thread_abort_sig, &sa, NULL) != -1);
	}
#endif

	initialized = TRUE;
}

int
mono_debugger_server_get_pending_sigint (void)
{
	if (pending_sigint > 0)
		return pending_sigint--;

	return 0;
}

void
mono_debugger_server_sem_init (void)
{
	sem_init (&manager_semaphore, 1, 0);
}

void
mono_debugger_server_sem_wait (void)
{
	sem_wait (&manager_semaphore);
}

void
mono_debugger_server_sem_post (void)
{
	sem_post (&manager_semaphore);
}

int
mono_debugger_server_sem_get_value (void)
{
	int ret;

	sem_getvalue (&manager_semaphore, &ret);
	return ret;
}
