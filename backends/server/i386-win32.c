#define _GNU_SOURCE
#include <server.h>
#include <breakpoints.h>
#include <i386-arch.h>
#include <sys/stat.h>
#include <signal.h>
#include <unistd.h>
#include <string.h>
#include <fcntl.h>
#include <errno.h>
#include <w32api.h>
#include <windows.h>

/*
 * NOTE:  The manpage is wrong about the POKE_* commands - the last argument
 *        is the data (a word) to be written, not a pointer to it.
 *
 * In general, the ptrace(2) manpage is very bad, you should really read
 * kernel/ptrace.c and arch/i386/kernel/ptrace.c in the Linux source code
 * to get a better understanding for this stuff.
 */

#include "i386-arch.h"

#define INFERIOR_REGS_TYPE	CONTEXT
#define INFERIOR_FPREGS_TYPE	CONTEXT

#define FLAG_TRACE_BIT		0x100

#define INFERIOR_REG_EIP(r)	r.Eip
#define INFERIOR_REG_ESP(r)	r.Esp
#define INFERIOR_REG_EBP(r)	r.Ebp
#define INFERIOR_REG_EAX(r)	r.Eax
#define INFERIOR_REG_EBX(r)	r.Ebx
#define INFERIOR_REG_ECX(r)	r.Ecx
#define INFERIOR_REG_EDX(r)	r.Edx
#define INFERIOR_REG_ESI(r)	r.Esi
#define INFERIOR_REG_EDI(r)	r.Edi
#define INFERIOR_REG_EFLAGS(r)	r.EFlags
#define INFERIOR_REG_ESP(r)	r.Esp
#define INFERIOR_REG_FS(r)	r.SegFs
#define INFERIOR_REG_ES(r)	r.SegEs
#define INFERIOR_REG_DS(r)	r.SegDs
#define INFERIOR_REG_CS(r)	r.SegCs
#define INFERIOR_REG_SS(r)	r.SegSs
#define INFERIOR_REG_GS(r)	r.SegGs

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
	long call_address;
	guint64 callback_argument;
	INFERIOR_REGS_TYPE current_regs;
	INFERIOR_FPREGS_TYPE current_fpregs;
	INFERIOR_REGS_TYPE *saved_regs;
	INFERIOR_FPREGS_TYPE *saved_fpregs;
	GPtrArray *rti_stack;
	unsigned dr_control, dr_status;
	BreakpointManager *bpm;
};

static ServerCommandError
get_registers (InferiorHandle *handle, INFERIOR_REGS_TYPE *regs)
{
	handle->current_regs.ContextFlags = CONTEXT_FULL;
	if (!GetThreadContext (handle->hThread, &handle->current_regs)) {
		g_message (G_STRLOC ": %d - %d", handle->dwThreadId, GetLastError ());
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
set_registers (InferiorHandle *handle, INFERIOR_REGS_TYPE *regs)
{
	if (!SetThreadContext (handle->hThread, &handle->current_regs)) {
		g_message (G_STRLOC ": %d - %d", handle->dwThreadId, GetLastError ());
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
get_fp_registers (InferiorHandle *handle, INFERIOR_FPREGS_TYPE *regs)
{
	return COMMAND_ERROR_UNKNOWN;
}

static ServerCommandError
set_fp_registers (InferiorHandle *handle, INFERIOR_FPREGS_TYPE *regs)
{
	return COMMAND_ERROR_UNKNOWN;
}

static ServerCommandError
server_read_data (InferiorHandle *handle, guint64 start, guint32 size, gpointer buffer)
{
	if (!ReadProcessMemory (handle->hProcess, (LPCVOID) start, (LPVOID) buffer, size, NULL)) {
		g_message (G_STRLOC ": %d - %d", handle->dwThreadId, GetLastError ());
		return COMMAND_ERROR_MEMORY_ACCESS;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_write_data (InferiorHandle *handle, guint64 start, guint32 size, gconstpointer buffer)
{
	if (!WriteProcessMemory (handle->hProcess, (LPCVOID) start, (LPVOID) buffer, size, NULL)) {
		g_message (G_STRLOC ": %d - %d", handle->dwThreadId, GetLastError ());
		return COMMAND_ERROR_MEMORY_ACCESS;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_peek_word (InferiorHandle *handle, guint64 start, int *retval)
{
	return server_read_data (handle, start, sizeof (int), retval);
}

static ServerCommandError
server_set_dr (InferiorHandle *handle, int regnum, unsigned long value)
{
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_continue (InferiorHandle *handle)
{
	int ret;

	ret = ContinueDebugEvent (handle->dwProcessId, handle->dwThreadId, DBG_CONTINUE);
	if (!ret) {
		g_message (G_STRLOC ": %d", GetLastError ());
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_step (InferiorHandle *handle)
{
	int ret;

	handle->current_regs.EFlags |= FLAG_TRACE_BIT;
	set_registers (handle, &handle->current_regs);

	ret = ContinueDebugEvent (handle->dwProcessId, handle->dwThreadId, DBG_CONTINUE);
	if (!ret) {
		g_message (G_STRLOC ": %d", GetLastError ());
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

#include "i386-arch.c"

static ServerCommandError
server_win32_spawn (InferiorHandle *handle, const gchar *working_directory, gchar **argv, gchar **envp,
		    gint *child_pid, ChildOutputFunc stdout_handler, ChildOutputFunc stderr_handler,
		    gchar **error)
{
	BOOL ret, done;
	STARTUPINFO si;
	PROCESS_INFORMATION pi;
	DEBUG_EVENT devent;
	gchar *cmd_line;

	cmd_line = g_strjoinv (" ", argv);

	ZeroMemory (&si, sizeof (si));
	si.cb = sizeof (si);
	ZeroMemory (&pi, sizeof (pi));

	ret = CreateProcess (NULL, cmd_line, NULL, NULL, FALSE, DEBUG_PROCESS, NULL, NULL, &si, &pi);

	g_message (G_STRLOC ": %d - %p,%d,%d", ret, pi.hProcess, pi.dwProcessId, pi.dwThreadId);

	*error = NULL;
	*child_pid = pi.dwProcessId;

	handle->hProcess = pi.hProcess;
	handle->hThread = pi.hThread;
	handle->dwProcessId = pi.dwProcessId;
	handle->dwThreadId = pi.dwThreadId;

	server_win32_wait (handle, NULL, NULL, NULL, NULL);
	get_registers (handle, &handle->current_regs);

	return COMMAND_ERROR_NONE;
}

static InferiorHandle *
server_win32_initialize (BreakpointManager *bpm)
{
	InferiorHandle *handle = g_new0 (InferiorHandle, 1);

	handle->bpm = bpm;
	handle->rti_stack = g_ptr_array_new ();
	return handle;
}

static void
server_win32_wait (InferiorHandle *handle, ServerStatusMessageType *type, guint64 *arg,
		   guint64 *data1, guint64 *data2)
{
	EXCEPTION_RECORD *erec;
	DEBUG_EVENT devent;
	int ret, i;

 again:
	ret = WaitForDebugEvent (&devent, INFINITE);
	g_message (G_STRLOC ": %d - %d,%d - %d", ret, devent.dwProcessId,
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

	g_message (G_STRLOC ": Exception event %d - %x - %p - %d,%d", devent.u.Exception.dwFirstChance,
		   erec->ExceptionCode, erec->ExceptionAddress, erec->ExceptionFlags,
		   erec->NumberParameters);

	for (i = 0; i < erec->NumberParameters; i++)
		g_message (G_STRLOC ": param %d - %p", i, erec->ExceptionInformation [i]);

	get_registers (handle, &handle->current_regs);
	g_message (G_STRLOC ": %p", INFERIOR_REG_EIP (handle->current_regs));

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

/*
 * Method VTable for this backend.
 */
InferiorInfo i386_win32_inferior = {
	server_win32_initialize,
	server_win32_spawn,

	NULL,
	NULL,
	NULL,
	server_win32_wait,

	i386_arch_get_target_info,
	server_continue,
	server_step,

	i386_arch_get_pc,

	NULL,
	server_read_data,
	server_write_data,

	NULL,
	NULL,
	NULL,

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

	NULL,
};
