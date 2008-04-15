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

ServerHandle *
mono_debugger_server_create_inferior (BreakpointManager *breakpoint_manager)
{
	if ((getuid () == 0) || (geteuid () == 0)) {
		g_message ("WARNING: Running mdb as root may be a problem because setuid() and\n"
			   "seteuid() do nothing.\n"
			   "See http://primates.ximian.com/~martin/blog/entry_150.html for details.");
	}
	return global_vtable->create_inferior (breakpoint_manager);
}

guint32
mono_debugger_server_get_current_pid (void)
{
	return getpid ();
}

guint64
mono_debugger_server_get_current_thread (void)
{
	return pthread_self ();
}

void
mono_debugger_server_io_thread_main (IOThreadData *io_data, ChildOutputFunc func)
{
	(global_vtable->io_thread_main) (io_data, func);
}

ServerCommandError
mono_debugger_server_spawn (ServerHandle *handle, const gchar *working_directory,
			    const gchar **argv, const gchar **envp, gint *child_pid,
			    IOThreadData **io_data, gchar **error)
{
	if (!global_vtable->spawn)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->spawn) (handle, working_directory, argv, envp,
					 child_pid, io_data, error);
}

ServerCommandError
mono_debugger_server_initialize_process (ServerHandle *handle)
{
	if (!global_vtable->initialize_process)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->initialize_process) (handle);
}

ServerCommandError
mono_debugger_server_initialize_thread (ServerHandle *handle, guint32 pid)
{
	if (!global_vtable->initialize_thread)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->initialize_thread) (handle, pid);
}

ServerCommandError
mono_debugger_server_attach (ServerHandle *handle, guint32 pid)
{
	if (!global_vtable->attach)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->attach) (handle, pid);
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
				     guint64 *data1, guint64 *data2, guint32 *opt_data_size,
				     gpointer *opt_data)
{
	return (*global_vtable->dispatch_event) (
		handle, status, arg, data1, data2, opt_data_size, opt_data);
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
mono_debugger_server_get_frame (ServerHandle *handle, StackFrame *frame)
{
	if (!global_vtable->get_frame)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->get_frame) (handle, frame);
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
mono_debugger_server_peek_word (ServerHandle *handle, guint64 start, guint64 *word)
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
				    guint64 method_argument, guint64 data_argument,
				    guint64 data_argument2, const gchar *string_argument,
				    guint64 callback_argument)
{
	if (!global_vtable->call_method_1)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->call_method_1) (
		handle, method_address, method_argument, data_argument,
		data_argument2, string_argument, callback_argument);
}

ServerCommandError
mono_debugger_server_call_method_2 (ServerHandle *handle, guint64 method_address,
				    guint32 data_size, gconstpointer data_buffer,
				    guint64 callback_argument)
{
	if (!global_vtable->call_method_2)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->call_method_2) (
		handle, method_address, data_size, data_buffer, callback_argument);
}

ServerCommandError
mono_debugger_server_call_method_invoke (ServerHandle *handle, guint64 invoke_method,
					 guint64 method_argument, guint32 num_params,
					 guint32 blob_size, guint64 *param_data,
					 gint32 *offset_data, gconstpointer blob_data,
					 guint64 callback_argument, gboolean debug)
{
	if (!global_vtable->call_method_invoke)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->call_method_invoke) (
		handle, invoke_method, method_argument, num_params, blob_size,
		param_data, offset_data, blob_data, callback_argument, debug);
}

ServerCommandError
mono_debugger_server_execute_instruction (ServerHandle *handle, const guint8 *instruction,
					  guint32 insn_size, gboolean update_ip)
{
	if (!global_vtable->execute_instruction)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->execute_instruction) (
		handle, instruction, insn_size, update_ip);
}

ServerCommandError
mono_debugger_server_mark_rti_frame (ServerHandle *handle)
{
	if (!global_vtable->mark_rti_frame)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->mark_rti_frame) (handle);
}

ServerCommandError
mono_debugger_server_abort_invoke (ServerHandle *handle, guint64 stack_pointer)
{
	if (!global_vtable->abort_invoke)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->abort_invoke) (handle, stack_pointer);
}

ServerCommandError
mono_debugger_server_insert_breakpoint (ServerHandle *handle, guint64 address, guint32 *breakpoint)
{
	if (!global_vtable->insert_breakpoint)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->insert_breakpoint) (handle, address, breakpoint);
}

ServerCommandError
mono_debugger_server_insert_hw_breakpoint (ServerHandle *handle, guint32 type, guint32 *idx,
					   guint64 address, guint32 *breakpoint)
{
	if (!global_vtable->insert_hw_breakpoint)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->insert_hw_breakpoint) (
		handle, type, idx, address, breakpoint);
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
mono_debugger_server_get_registers (ServerHandle *handle, guint64 *values)
{
	if (!global_vtable->get_registers)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->get_registers) (handle, values);
}

ServerCommandError
mono_debugger_server_set_registers (ServerHandle *handle, guint64 *values)
{
	if (!global_vtable->set_registers)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->set_registers) (handle, values);
}

ServerCommandError
mono_debugger_server_stop (ServerHandle *handle)
{
	if (!global_vtable->stop)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->stop) (handle);
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
mono_debugger_server_get_signal_info (ServerHandle *handle, SignalInfo **sinfo)
{
	*sinfo = NULL;

	if (!global_vtable->get_signal_info)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->get_signal_info) (handle, sinfo);

}

void
mono_debugger_server_set_runtime_info (ServerHandle *handle, MonoRuntimeInfo *mono_runtime)
{
	if (global_vtable->set_runtime_info)
		return (* global_vtable->set_runtime_info) (handle, mono_runtime);
}

ServerCommandError
mono_debugger_server_get_threads (ServerHandle *handle, guint32 *count, guint32 **threads)
{
	if (!global_vtable->get_threads)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->get_threads) (handle, count, threads);
}

ServerCommandError
mono_debugger_server_get_application (ServerHandle *handle, gchar **exe_file, gchar **cwd,
				      guint32 *nargs, gchar ***cmdline_args)
{
	if (!global_vtable->get_application)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->get_application) (handle, exe_file, cwd, nargs, cmdline_args);
}

ServerCommandError
mono_debugger_server_detach_after_fork (ServerHandle *handle)
{
	if (!global_vtable->detach_after_fork)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->detach_after_fork) (handle);
}

ServerCommandError
mono_debugger_server_push_registers (ServerHandle *handle, guint64 *new_rsp)
{
	if (!global_vtable->push_registers)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->push_registers) (handle, new_rsp);
}

ServerCommandError
mono_debugger_server_pop_registers (ServerHandle *handle)
{
	if (!global_vtable->pop_registers)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->pop_registers) (handle);
}

ServerCommandError
mono_debugger_server_get_callback_frame (ServerHandle *handle, guint64 stack_pointer,
					 gboolean exact_match, guint64 *registers)
{
	if (!global_vtable->get_callback_frame)
		return COMMAND_ERROR_NOT_IMPLEMENTED;

	return (* global_vtable->get_callback_frame) (
		handle, stack_pointer, exact_match, registers);
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

void
mono_debugger_server_static_init (void)
{
	struct sigaction sa;

	if (initialized)
		return;

	/* catch SIGINT */
	sa.sa_handler = sigint_signal_handler;
	sigemptyset (&sa.sa_mask);
	sa.sa_flags = 0;
	g_assert (sigaction (SIGINT, &sa, NULL) != -1);

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
