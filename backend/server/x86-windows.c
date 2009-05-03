#include <server.h>
#include "x86-arch.h"
#include <windows.h>
#include <Psapi.h>
#include <stdio.h>

struct ArchInfo
{
	//INFERIOR_REGS_TYPE current_regs;
	//INFERIOR_FPREGS_TYPE current_fpregs;
	GPtrArray *callback_stack;
	//CodeBufferData *code_buffer;
	//guint64 dr_control, dr_status;
	int dr_regs [DR_NADDR];
};

ArchInfo *
x86_arch_initialize (void)
{
	ArchInfo *arch = g_new0 (ArchInfo, 1);

	arch->callback_stack = g_ptr_array_new ();

	return arch;
}

static gboolean initialized = FALSE;
static HANDLE manager_semaphore;
static LONG sem_count = 0;

static BOOL WINAPI HandlerRoutine(DWORD dwCtrlType)
{
	ReleaseSemaphore(manager_semaphore, 1, &sem_count);
	return TRUE;
}

static void
server_win32_static_init (void)
{
	if (initialized)
		return;

	SetConsoleCtrlHandler(HandlerRoutine, TRUE);
	g_thread_init (NULL);

	initialized = TRUE;
}


static ServerCommandError
server_win32_get_signal_info (ServerHandle *handle, SignalInfo **sinfo_out)
{
	SignalInfo *sinfo = g_new0 (SignalInfo, 1);

	*sinfo_out = sinfo;

	return COMMAND_ERROR_NONE;
}


static void
server_win32_global_init (void)
{
}

struct InferiorHandle
{
	guint32 pid;
	HANDLE process_handle;
	HANDLE thread_handle;
	gint argc;
	gchar **argv;
//#ifdef __linux__
//	int mem_fd;
//#endif
//	int stepping;
//	int last_signal;
	int redirect_fds;
	int output_fd [2], error_fd [2];
	int is_thread, is_initialized;
};

static ServerHandle *
server_win32_create_inferior (BreakpointManager *bpm)
{
	ServerHandle *handle = g_new0 (ServerHandle, 1);

	handle->bpm = bpm;
	handle->inferior = g_new0 (InferiorHandle, 1);
	handle->arch = x86_arch_initialize ();

	return handle;
}

static ServerCommandError
server_win32_get_target_info (guint32            *target_int_size,
							  guint32            *target_long_size,
							  guint32            *target_address_size,
							  guint32            *is_bigendian)
{
	*target_int_size = sizeof (guint32);
	*target_long_size = sizeof (guint32);
	*target_address_size = sizeof (void *);
	*is_bigendian = 0;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_spawn (ServerHandle *handle, const gchar *working_directory,
		     const gchar **argv, const gchar **envp, gboolean redirect_fds,
			 gint *child_pid, IOThreadData **io_data, gchar **error)
{	
	InferiorHandle *inferior = handle->inferior;
	int ret;
	STARTUPINFO si;
	PROCESS_INFORMATION pi;
	gunichar2* utf16_argv = NULL;
	gunichar2* utf16_envp = NULL;
	gunichar2* utf16_working_directory = NULL;

	memset (&si, 0, sizeof (si));
	si.cb = sizeof(si);
	*error = NULL;

	if (working_directory) {
		utf16_working_directory = g_utf8_to_utf16 (working_directory, -1, NULL, NULL, NULL);
	}

	if (envp) {
		guint len = 0;
		const gchar** envp_temp = envp;
		gunichar2* envp_concat;

		while (*envp_temp) {
			len += strlen(*envp_temp) + 1;
			envp_temp++;
		}
		len++; /* add one for double NULL at end */
		envp_concat = utf16_envp = g_malloc(len*sizeof(gunichar2));

		envp_temp = envp;
		while (*envp_temp) {
			gunichar2* utf16_envp_temp = g_utf8_to_utf16 (*envp_temp, -1, NULL, NULL, NULL);
			int written = swprintf(envp_concat, len, L"%s%s", utf16_envp_temp, L"\0");
			g_free(utf16_envp_temp);
			envp_concat += written + 1;
			len -= written;
			envp_temp++;
		}
		swprintf (envp_concat, len, L"%s", L"\0"); /* double NULL at end */
	}

	if (argv) {
		gint argc = 0;
		guint len = 0;
		gint index = 0;
		const gchar** argv_temp = argv;
		gunichar2* argv_concat;

		while (*argv_temp) {
			len += strlen(*argv_temp) + 1;
			argv_temp++;
			argc++;
		}
		inferior->argc = argc;
		inferior->argv = g_malloc0 ((argc+1) * sizeof (gpointer));
		argv_concat = utf16_argv = g_malloc (len*sizeof(gunichar2));

		argv_temp = argv;
		while (*argv_temp) {
			gunichar2* utf16_argv_temp = g_utf8_to_utf16 (*argv_temp, -1, NULL, NULL, NULL);
			int written = swprintf(argv_concat, len, L"%s ", utf16_argv_temp);
			inferior->argv[index++] = g_strdup (*argv_temp);
			g_free(utf16_argv_temp);
			argv_concat += written;
			len -= written;
			argv_temp++;
		}
	}

	wprintf(L"Spawning process with\nCommand line: %s\nWorking Directory: %s\nThread Id: %d\n", 
		utf16_argv, utf16_working_directory, GetCurrentThreadId());

	ret = CreateProcess(NULL, utf16_argv, NULL, NULL, FALSE, DEBUG_PROCESS | CREATE_UNICODE_ENVIRONMENT, 
		utf16_envp, utf16_working_directory, &si, &pi);

	if (!ret)
	{
		int erro_code = GetLastError();
		*error = g_strdup_printf ("CreateProcess failed: %d", erro_code);
		return COMMAND_ERROR_CANNOT_START_TARGET;
	}

	*child_pid = pi.dwProcessId;

	inferior->pid = pi.dwProcessId;
	inferior->process_handle = pi.hProcess;
	inferior->thread_handle = pi.hThread;

	return COMMAND_ERROR_NONE;
}

void
server_win32_io_thread_main (IOThreadData *io_data, ChildOutputFunc func)
{
	Sleep(600000);
}

static guint32
server_win32_global_wait (guint32 *status_ret)
{
	int ret;
	DEBUG_EVENT de;

	printf("WaitForDebugEvent on thread %d\n", GetCurrentThreadId());
	ret = WaitForDebugEvent(&de, 100);
	if (!ret)
		return 0;

	*status_ret = de.dwDebugEventCode;

	return de.dwProcessId;
}


static ServerCommandError
server_win32_get_frame (ServerHandle *handle, StackFrame *frame)
{
	ServerCommandError result;

	CONTEXT context;
	memset (&context, 0, sizeof (CONTEXT));
	context.ContextFlags = CONTEXT_CONTROL;
	if (!GetThreadContext (handle->inferior->thread_handle, &context))
		return COMMAND_ERROR_INTERNAL_ERROR;

	frame->address = (guint32) context.Eip;
	frame->stack_pointer = (guint32) context.Esp;
	frame->frame_address = (guint32) context.Ebp;
	return COMMAND_ERROR_NONE;
}


static ServerCommandError
server_win32_initialize_process (ServerHandle *handle)
{
	return COMMAND_ERROR_NONE;
}

static ServerStatusMessageType
server_win32_dispatch_event (ServerHandle *handle, guint32 status, guint64 *arg,
			      guint64 *data1, guint64 *data2, guint32 *opt_data_size,
			      gpointer *opt_data)
{
	if (status == CREATE_PROCESS_DEBUG_EVENT) {
		return MESSAGE_CHILD_EXECD;
	}

	return MESSAGE_UNKNOWN_ERROR;
}

void GetProcessStrings(HANDLE hProcess, LPWSTR lpszCmdLine, LPWSTR lpszEnvVars);

static ServerCommandError
server_win32_get_application (ServerHandle *handle, gchar **exe_file, gchar **cwd,
			       guint32 *nargs, gchar ***cmdline_args)
{
	gint index = 0;
	GPtrArray *array;
	gchar **ptr;
	/* No supported way to get command line of a process
	   see http://blogs.msdn.com/oldnewthing/archive/2009/02/23/9440784.aspx */

/*	gunichar2 utf16_exe_file[1024];
	gunichar2 utf16_cmd_line[10240];
	gunichar2 utf16_env_vars[10240];
	BOOL ret;
	if (!GetModuleFileNameEx (handle->inferior->process_handle, NULL, utf16_exe_file, sizeof(utf16_exe_file)/sizeof(utf16_exe_file[0]))) {
		DWORD error = GetLastError ();
		return COMMAND_ERROR_INTERNAL_ERROR;
	}
	*/
	*exe_file = g_strdup (handle->inferior->argv[0]);
	*nargs = handle->inferior->argc;

	array = g_ptr_array_new ();

	for (index = 0; index < handle->inferior->argc; index++)
		g_ptr_array_add (array, handle->inferior->argv[index]);

	*cmdline_args = ptr = g_new0 (gchar *, array->len + 1);

	for (index = 0; index < array->len; index++)
		ptr  [index] = g_ptr_array_index (array, index);

	g_ptr_array_free (array, FALSE);

	return COMMAND_ERROR_NONE;
}

static guint32
server_win32_get_current_pid (void)
{
	return GetCurrentProcessId();
}

static guint64
server_win32_get_current_thread (void)
{
	return GetCurrentThreadId ();
}

static int pending_sigint = 0;

static void
server_win32_sem_init (void)
{
	manager_semaphore = CreateSemaphore( 
        NULL,           // default security attributes
        0,  // initial count
        12,  // maximum count
        NULL);          // unnamed semaphore
}

static void
server_win32_sem_wait (void)
{
	WaitForSingleObject( manager_semaphore, INFINITE);
}

static void
server_win32_sem_post (void)
{
	ReleaseSemaphore(manager_semaphore, 1, &sem_count);
}

static int
server_win32_sem_get_value (void)
{
	return sem_count;
}

static int
server_win32_get_pending_sigint (void)
{
	if (pending_sigint > 0)
		return pending_sigint--;

	return 0;
}

InferiorVTable i386_windows_inferior = {
	server_win32_static_init,			/*static_init, */
	server_win32_global_init,			/*global_init, */
	server_win32_create_inferior,		/*create_inferior, */
	server_win32_initialize_process,	/*initialize_process, */
	NULL,					 			/*initialize_thread, */
	NULL,					 			/*set_runtime_info, */
	server_win32_io_thread_main,		/*io_thread_main, */
	server_win32_spawn,					/*spawn, */
	NULL,		 						/*attach, */
	NULL,					 			/*detach, */
	NULL,					 			/*finalize, */
	server_win32_global_wait,			/*global_wait, */
	NULL,					 			/*stop_and_wait, */
	server_win32_dispatch_event,		/*dispatch_event, */
	NULL,								/*dispatch_simple, */
	server_win32_get_target_info,		/*get_target_info, */
	NULL,					 			/*continue, */
	NULL,					 			/*step, */
	NULL,					 			/*resume, */
	server_win32_get_frame,	 			/*get_frame, */
	NULL,					 			/*current_insn_is_bpt, */
	NULL,					 			/*peek_word, */
	NULL,					 			/*read_memory, */
	NULL,					 			/*write_memory, */
	NULL,					 			/*call_method, */
	NULL,					 			/*call_method_1, */
	NULL,					 			/*call_method_2, */
	NULL,					 			/*call_method_3, */
	NULL,					 			/*call_method_invoke, */
	NULL,					 			/*execute_instruction, */
	NULL,					 			/*mark_rti_frame, */
	NULL,					 			/*abort_invoke, */
	NULL,					 			/*insert_breakpoint, */
	NULL,					 			/*insert_hw_breakpoint, */
	NULL,					 			/*remove_breakpoint, */
	NULL,					 			/*enable_breakpoint, */
	NULL,					 			/*disable_breakpoint, */
	NULL,					 			/*get_breakpoints, */
	NULL,					 			/*get_registers, */
	NULL,					 			/*set_registers, */
	NULL,					 			/*stop, */
	NULL,					 			/*set_signal, */
	NULL,					 			/*server_ptrace_get_pending_signal, */
	NULL,					 			/*kill, */
	server_win32_get_signal_info,					 			/*get_signal_info, */
	NULL,					 			/*get_threads, */
	server_win32_get_application,		/*get_application, */
	NULL,								/*detach_after_fork, */
	NULL,								/*push_registers, */
	NULL,								/*pop_registers, */
	NULL,								/*get_callback_frame, */
	NULL,								/*get_registers_from_core_file, */
	server_win32_get_current_pid,		/*get_current_pid, */
	server_win32_get_current_thread,	/*get_current_thread, */
	server_win32_sem_init,				/*sem_init, */
	server_win32_sem_wait,				/*sem_wait, */
	server_win32_sem_post,				/*sem_post, */
	server_win32_sem_get_value,			/*sem_get_value, */
	server_win32_get_pending_sigint,	/*get_pending_sigint, */
	};
