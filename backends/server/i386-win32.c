#define _GNU_SOURCE
#include <server.h>
#include <breakpoints.h>
#include <sys/stat.h>
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

#include "i386-win32.h"
#include "i386-arch.h"

typedef struct
{
	INFERIOR_REGS_TYPE *saved_regs;
	INFERIOR_FPREGS_TYPE *saved_fpregs;
	long call_address;
	guint64 callback_argument;
} RuntimeInvokeData;

struct InferiorHandle
{
	HANDLE hProcess, hThread;
	DWORD dwProcessId, dwThreadId;
	InferiorInfo *inferior;
	unsigned dr_control, dr_status;
	BreakpointManager *bpm;
};

static ServerCommandError
server_get_registers (InferiorHandle *handle, INFERIOR_REGS_TYPE *regs)
{
	regs->ContextFlags = CONTEXT_FULL;
	if (!GetThreadContext (handle->hThread, regs)) {
		g_message (G_STRLOC ": %ld - %ld", handle->dwThreadId, GetLastError ());
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_set_registers (InferiorHandle *handle, INFERIOR_REGS_TYPE *regs)
{
	if (!SetThreadContext (handle->hThread, regs)) {
		g_message (G_STRLOC ": %ld - %ld", handle->dwThreadId, GetLastError ());
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_get_fp_registers (InferiorHandle *handle, INFERIOR_FPREGS_TYPE *regs)
{
	return COMMAND_ERROR_UNKNOWN_ERROR;
}

static ServerCommandError
server_set_fp_registers (InferiorHandle *handle, INFERIOR_FPREGS_TYPE *regs)
{
	return COMMAND_ERROR_UNKNOWN_ERROR;
}

static ServerCommandError
server_read_data (InferiorHandle *handle, ArchInfo *arch, guint64 start, guint32 size, gpointer buffer)
{
	if (!ReadProcessMemory (handle->hProcess, (LPCVOID) start, (LPVOID) buffer, size, NULL)) {
		g_message (G_STRLOC ": %ld - %ld", handle->dwThreadId, GetLastError ());
		return COMMAND_ERROR_MEMORY_ACCESS;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_write_data (InferiorHandle *handle, ArchInfo *arch, guint64 start, guint32 size, gconstpointer buffer)
{
	if (!WriteProcessMemory (handle->hProcess, (LPCVOID) start, (LPVOID) buffer, size, NULL)) {
		g_message (G_STRLOC ": %ld - %ld", handle->dwThreadId, GetLastError ());
		return COMMAND_ERROR_MEMORY_ACCESS;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_peek_word (InferiorHandle *handle, ArchInfo *arch, guint64 start, int *retval)
{
	return server_read_data (handle, arch, start, sizeof (int), retval);
}

static ServerCommandError
server_set_dr (InferiorHandle *handle, int regnum, unsigned long value)
{
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_continue (InferiorHandle *handle, ArchInfo *arch)
{
	int ret;

	ret = ContinueDebugEvent (handle->dwProcessId, handle->dwThreadId, DBG_CONTINUE);
	if (!ret) {
		g_message (G_STRLOC ": %ld", GetLastError ());
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_step (InferiorHandle *handle, ArchInfo *arch)
{
	int ret;

	arch->current_regs.EFlags |= FLAG_TRACE_BIT;
	server_set_registers (handle, &arch->current_regs);

	ret = ContinueDebugEvent (handle->dwProcessId, handle->dwThreadId, DBG_CONTINUE);
	if (!ret) {
		g_message (G_STRLOC ": %ld", GetLastError ());
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	return COMMAND_ERROR_NONE;
}

static void
server_win32_finalize (InferiorHandle *handle, ArchInfo *arch)
{
	i386_arch_finalize (arch);
	g_free (handle);
}

#include "i386-arch.c"

static ServerCommandError
server_win32_spawn (InferiorHandle *handle, ArchInfo *arch, const gchar *working_directory, gchar **argv,
		    gchar **envp, gint *child_pid, ChildOutputFunc stdout_handler,
		    ChildOutputFunc stderr_handler, gchar **error)
{
	BOOL ret;
	STARTUPINFO si;
	PROCESS_INFORMATION pi;
	gchar *cmd_line;

	cmd_line = g_strjoinv (" ", argv);

	ZeroMemory (&si, sizeof (si));
	si.cb = sizeof (si);
	ZeroMemory (&pi, sizeof (pi));

	ret = CreateProcess (NULL, cmd_line, NULL, NULL, FALSE, DEBUG_PROCESS, NULL, NULL, &si, &pi);

	g_message (G_STRLOC ": %d - %p,%ld,%ld", ret, pi.hProcess, pi.dwProcessId, pi.dwThreadId);

	*error = NULL;
	*child_pid = pi.dwProcessId;

	handle->hProcess = pi.hProcess;
	handle->hThread = pi.hThread;
	handle->dwProcessId = pi.dwProcessId;
	handle->dwThreadId = pi.dwThreadId;

	server_win32_wait (handle, arch, NULL, NULL, NULL, NULL);
	server_get_registers (handle, &arch->current_regs);

	return COMMAND_ERROR_NONE;
}

static InferiorHandle *
server_win32_initialize (BreakpointManager *bpm)
{
	InferiorHandle *handle = g_new0 (InferiorHandle, 1);

	handle->inferior = &i386_win32_inferior;
	handle->bpm = bpm;
	return handle;
}

static void
server_win32_wait (InferiorHandle *handle, ArchInfo *arch, ServerStatusMessageType *type,
		   guint64 *arg, guint64 *data1, guint64 *data2)
{
	EXCEPTION_RECORD *erec;
	DEBUG_EVENT devent;
	int ret, i;

 again:
	ret = WaitForDebugEvent (&devent, INFINITE);
	g_message (G_STRLOC ": %d - %ld,%ld - %ld", ret, devent.dwProcessId,
		   devent.dwThreadId, devent.dwDebugEventCode);

	if ((devent.dwProcessId != handle->dwProcessId) || (devent.dwThreadId != handle->dwThreadId)) {
		ContinueDebugEvent (devent.dwProcessId, devent.dwThreadId, DBG_CONTINUE);
		goto again;
	}

	if (devent.dwDebugEventCode == OUTPUT_DEBUG_STRING_EVENT) {
		OUTPUT_DEBUG_STRING_INFO *ds = &devent.u.DebugString;
		g_message (G_STRLOC ": debug string %p - %d,%d", ds->lpDebugStringData,
			   ds->fUnicode, ds->nDebugStringLength);
		ContinueDebugEvent (handle->dwProcessId, handle->dwThreadId, DBG_CONTINUE);
		goto again;
	}

	if ((devent.dwDebugEventCode == CREATE_PROCESS_DEBUG_EVENT) ||
	    (devent.dwDebugEventCode == CREATE_THREAD_DEBUG_EVENT) ||
	    (devent.dwDebugEventCode == LOAD_DLL_DEBUG_EVENT)) {
		ContinueDebugEvent (handle->dwProcessId, handle->dwThreadId, DBG_CONTINUE);
		goto again;
	}

	g_assert (devent.dwDebugEventCode == EXCEPTION_DEBUG_EVENT);

	erec = &devent.u.Exception.ExceptionRecord;

	g_message (G_STRLOC ": Exception event %ld - %lx - %p - %ld,%ld", devent.u.Exception.dwFirstChance,
		   erec->ExceptionCode, erec->ExceptionAddress, erec->ExceptionFlags,
		   erec->NumberParameters);

	for (i = 0; i < erec->NumberParameters; i++)
		g_message (G_STRLOC ": param %d - %lx", i, erec->ExceptionInformation [i]);

	server_get_registers (handle, &arch->current_regs);
	g_message (G_STRLOC ": %lx", INFERIOR_REG_EIP (arch->current_regs));

	if (!type)
		return;

	switch (erec->ExceptionCode) {
	case EXCEPTION_BREAKPOINT:
		*type = MESSAGE_CHILD_HIT_BREAKPOINT;
		*arg = 0;
		g_message (G_STRLOC ": breakpoint");
		break;

	case EXCEPTION_SINGLE_STEP:
		*type = MESSAGE_CHILD_STOPPED;
		*arg = 0;
		g_message (G_STRLOC ": step");
		break;

	default:
		g_message (G_STRLOC ": crash");
		*type = MESSAGE_CHILD_STOPPED;
		*arg = erec->ExceptionCode & 0x00ffffff;
		break;
	}
}

static ServerCommandError
server_win32_get_signal_info (InferiorHandle *handle, ArchInfo *arch, SignalInfo *sinfo)
{
	sinfo->sigkill = 9;
	sinfo->sigstop = 17;
	sinfo->sigint = 2;
	sinfo->sigchld = 20;
	sinfo->sigprof = 27;
	sinfo->sigpwr = -1;
	sinfo->sigxcpu = 24;

	sinfo->thread_abort = -1;
	sinfo->thread_restart = -1;
	sinfo->thread_debug = -1;
	sinfo->mono_thread_debug = -1;

	return COMMAND_ERROR_NONE;
}

/*
 * Method VTable for this backend.
 */
InferiorInfo i386_win32_inferior = {
	i386_arch_initialize,
	server_win32_initialize,
	server_win32_spawn,

	NULL, // server_win32_attach,
	NULL, // server_win32_detach,
	server_win32_finalize,
	server_win32_wait,

	i386_arch_get_target_info,
	server_win32_continue,
	server_win32_step,

	i386_arch_get_pc,
	i386_arch_current_insn_is_bpt,

	server_read_data,
	server_write_data,

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

	NULL, // server_win32_stop,
	NULL, // server_win32_set_signal,
	NULL, // server_win32_kill,
	server_win32_get_signal_info
};
