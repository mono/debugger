#include <server.h>
#include <breakpoints.h>
#include <sys/stat.h>
#include <sys/ptrace.h>
#include <sys/socket.h>
#include <sys/wait.h>
#include <signal.h>
#include <unistd.h>
#include <string.h>
#include <fcntl.h>
#include <errno.h>

#include "x86-linux-ptrace.h"
#include "x86-arch.h"

struct ArchInfo
{
	long call_address;
	guint64 callback_argument;
	INFERIOR_REGS_TYPE current_regs;
	INFERIOR_FPREGS_TYPE current_fpregs;
	INFERIOR_REGS_TYPE *saved_regs;
	INFERIOR_FPREGS_TYPE *saved_fpregs;
	GPtrArray *rti_stack;
};

typedef struct
{
	INFERIOR_REGS_TYPE *saved_regs;
	INFERIOR_FPREGS_TYPE *saved_fpregs;
	long call_address;
	long exc_address;
	gboolean debug;
	guint64 callback_argument;
} RuntimeInvokeData;

static guint64 notification_address;

ArchInfo *
x86_arch_initialize (void)
{
	ArchInfo *arch = g_new0 (ArchInfo, 1);

	arch->rti_stack = g_ptr_array_new ();

	return arch;
}

void
x86_arch_finalize (ArchInfo *arch)
{
	g_ptr_array_free (arch->rti_stack, TRUE);
	g_free (arch->saved_regs);
     	g_free (arch->saved_fpregs);
	g_free (arch);
}

static void
server_ptrace_set_notification (guint64 addr)
{
	notification_address = addr;
}

static ServerCommandError
server_ptrace_current_insn_is_bpt (ServerHandle *handle, guint32 *is_breakpoint)
{
	mono_debugger_breakpoint_manager_lock ();
	if (mono_debugger_breakpoint_manager_lookup (handle->bpm, INFERIOR_REG_RIP (handle->arch->current_regs)))
		*is_breakpoint = TRUE;
	else
		*is_breakpoint = FALSE;
	mono_debugger_breakpoint_manager_unlock ();

	return COMMAND_ERROR_NONE;
}

void
x86_arch_remove_breakpoints_from_target_memory (ServerHandle *handle, guint64 start,
						guint32 size, gpointer buffer)
{
	GPtrArray *breakpoints;
	guint8 *ptr = buffer;
	int i;

	mono_debugger_breakpoint_manager_lock ();

	breakpoints = mono_debugger_breakpoint_manager_get_breakpoints (handle->bpm);
	for (i = 0; i < breakpoints->len; i++) {
		X86BreakpointInfo *info = g_ptr_array_index (breakpoints, i);
		guint64 offset;

		if (info->info.is_hardware_bpt || !info->info.enabled)
			continue;
		if ((info->info.address < start) || (info->info.address >= start+size))
			continue;

		offset = (guint64) info->info.address - start;
		ptr [offset] = info->saved_insn;
	}

	mono_debugger_breakpoint_manager_unlock ();
}

static ServerCommandError
server_ptrace_get_frame (ServerHandle *handle, StackFrame *frame)
{
	frame->address = (guint64) INFERIOR_REG_RIP (handle->arch->current_regs);
	frame->stack_pointer = (guint64) INFERIOR_REG_RSP (handle->arch->current_regs);
	frame->frame_address = (guint64) INFERIOR_REG_RBP (handle->arch->current_regs);
	return COMMAND_ERROR_NONE;
}

static gboolean
check_breakpoint (ServerHandle *handle, guint64 address, guint64 *retval)
{
	X86BreakpointInfo *info;

	mono_debugger_breakpoint_manager_lock ();
	info = (X86BreakpointInfo *) mono_debugger_breakpoint_manager_lookup (handle->bpm, address);
	if (!info || !info->info.enabled) {
		mono_debugger_breakpoint_manager_unlock ();
		return FALSE;
	}

	*retval = info->info.id;
	mono_debugger_breakpoint_manager_unlock ();
	return TRUE;
}

static RuntimeInvokeData *
get_runtime_invoke_data (ArchInfo *arch)
{
	if (!arch->rti_stack->len)
		return NULL;

	return g_ptr_array_index (arch->rti_stack, arch->rti_stack->len - 1);
}

static ServerCommandError
x86_arch_get_registers (ServerHandle *handle)
{
	ServerCommandError result;

	result = _server_ptrace_get_registers (handle->inferior, &handle->arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	_server_ptrace_get_fp_registers (handle->inferior, &handle->arch->current_fpregs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}

guint32
x86_arch_get_tid (ServerHandle *handle)
{
	guint64 start = INFERIOR_REG_RSP (handle->arch->current_regs) + 8;
	guint64 tid;

	if (server_ptrace_peek_word (handle, start, &tid) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't get tid");

	return (guint32) tid;
}

ChildStoppedAction
x86_arch_child_stopped (ServerHandle *handle, int stopsig,
			guint64 *callback_arg, guint64 *retval, guint64 *retval2)
{
	ArchInfo *arch = handle->arch;
	InferiorHandle *inferior = handle->inferior;
	RuntimeInvokeData *rdata;

	x86_arch_get_registers (handle);

	if (INFERIOR_REG_RIP (arch->current_regs) == notification_address) {
		*callback_arg = INFERIOR_REG_RDI (arch->current_regs);
		*retval = INFERIOR_REG_RSI (arch->current_regs);
		*retval2 = INFERIOR_REG_RDX (arch->current_regs);

		return STOP_ACTION_NOTIFICATION;
	}

	if (check_breakpoint (handle, INFERIOR_REG_RIP (arch->current_regs) - 1, retval)) {
		INFERIOR_REG_RIP (arch->current_regs)--;
		_server_ptrace_set_registers (inferior, &arch->current_regs);
		return STOP_ACTION_BREAKPOINT_HIT;
	}

	rdata = get_runtime_invoke_data (arch);
	if (rdata && (rdata->call_address == INFERIOR_REG_RIP (arch->current_regs))) {
		if (_server_ptrace_set_registers (inferior, rdata->saved_regs) != COMMAND_ERROR_NONE)
			g_error (G_STRLOC ": Can't restore registers after returning from a call");

		if (_server_ptrace_set_fp_registers (inferior, rdata->saved_fpregs) != COMMAND_ERROR_NONE)
			g_error (G_STRLOC ": Can't restore FP registers after returning from a call");

		*callback_arg = rdata->callback_argument;
		*retval = INFERIOR_REG_RAX (arch->current_regs);

		if (server_ptrace_peek_word (handle, rdata->exc_address, retval2) != COMMAND_ERROR_NONE)
			g_error (G_STRLOC ": Can't get exc object");

		g_free (rdata->saved_regs);
		g_free (rdata->saved_fpregs);
		g_ptr_array_remove (arch->rti_stack, rdata);

		x86_arch_get_registers (handle);

		if (rdata->debug) {
			*retval = 0;
			g_free (rdata);
			return STOP_ACTION_BREAKPOINT_HIT;
		}

		g_free (rdata);
		return STOP_ACTION_CALLBACK;
	}

	if (!arch->call_address || arch->call_address != INFERIOR_REG_RIP (arch->current_regs)) {
		guint64 code;

#if defined(__linux__) || defined(__FreeBSD__)
		if (stopsig != SIGTRAP)
			return STOP_ACTION_SEND_STOPPED;
#endif

		if (server_ptrace_peek_word (handle, GPOINTER_TO_SIZE(INFERIOR_REG_RIP (arch->current_regs) - 1), &code) != COMMAND_ERROR_NONE)
			return STOP_ACTION_SEND_STOPPED;

		if ((code & 0xff) == 0xcc) {
			*retval = 0;
			return STOP_ACTION_BREAKPOINT_HIT;
		}

		return STOP_ACTION_SEND_STOPPED;
	}

	if (_server_ptrace_set_registers (inferior, arch->saved_regs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't restore registers after returning from a call");

	if (_server_ptrace_set_fp_registers (inferior, arch->saved_fpregs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't restore FP registers after returning from a call");

	*callback_arg = arch->callback_argument;
	*retval = INFERIOR_REG_RAX (arch->current_regs);
	*retval2 = 0;

	g_free (arch->saved_regs);
	g_free (arch->saved_fpregs);

	arch->saved_regs = NULL;
	arch->saved_fpregs = NULL;
	arch->call_address = 0;
	arch->callback_argument = 0;

	x86_arch_get_registers (handle);

	return STOP_ACTION_CALLBACK;
}

static ServerCommandError
server_ptrace_get_target_info (guint32 *target_int_size, guint32 *target_long_size,
			       guint32 *target_address_size, guint32 *is_bigendian)
{
	*target_int_size = sizeof (long);
	*target_long_size = sizeof (guint64);
	*target_address_size = sizeof (void *);
	*is_bigendian = 0;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_get_registers (ServerHandle *handle, guint64 *values)
{
	ArchInfo *arch = handle->arch;

	values [DEBUGGER_REG_R15] = (guint64) INFERIOR_REG_R15 (arch->current_regs);
	values [DEBUGGER_REG_R14] = (guint64) INFERIOR_REG_R14 (arch->current_regs);
	values [DEBUGGER_REG_R13] = (guint64) INFERIOR_REG_R13 (arch->current_regs);
	values [DEBUGGER_REG_R12] = (guint64) INFERIOR_REG_R12 (arch->current_regs);
	values [DEBUGGER_REG_RBP] = (guint64) INFERIOR_REG_RBP (arch->current_regs);
	values [DEBUGGER_REG_RBX] = (guint64) INFERIOR_REG_RBX (arch->current_regs);
	values [DEBUGGER_REG_R11] = (guint64) INFERIOR_REG_R11 (arch->current_regs);
	values [DEBUGGER_REG_R10] = (guint64) INFERIOR_REG_R10 (arch->current_regs);
	values [DEBUGGER_REG_R9] = (guint64) INFERIOR_REG_R9 (arch->current_regs);
	values [DEBUGGER_REG_R8] = (guint64) INFERIOR_REG_R8 (arch->current_regs);
	values [DEBUGGER_REG_RAX] = (guint64) INFERIOR_REG_RAX (arch->current_regs);
	values [DEBUGGER_REG_RCX] = (guint64) INFERIOR_REG_RCX (arch->current_regs);
	values [DEBUGGER_REG_RDX] = (guint64) INFERIOR_REG_RDX (arch->current_regs);
	values [DEBUGGER_REG_RSI] = (guint64) INFERIOR_REG_RSI (arch->current_regs);
	values [DEBUGGER_REG_RDI] = (guint64) INFERIOR_REG_RDI (arch->current_regs);
	values [DEBUGGER_REG_ORIG_RAX] = (guint64) INFERIOR_REG_ORIG_RAX (arch->current_regs);
	values [DEBUGGER_REG_RIP] = (guint64) INFERIOR_REG_RIP (arch->current_regs);
	values [DEBUGGER_REG_CS] = (guint64) INFERIOR_REG_CS (arch->current_regs);
	values [DEBUGGER_REG_EFLAGS] = (guint64) INFERIOR_REG_EFLAGS (arch->current_regs);
	values [DEBUGGER_REG_RSP] = (guint64) INFERIOR_REG_RSP (arch->current_regs);
	values [DEBUGGER_REG_SS] = (guint64) INFERIOR_REG_SS (arch->current_regs);
	values [DEBUGGER_REG_FS_BASE] = (guint64) INFERIOR_REG_FS_BASE (arch->current_regs);
	values [DEBUGGER_REG_GS_BASE] = (guint64) INFERIOR_REG_GS_BASE (arch->current_regs);
	values [DEBUGGER_REG_DS] = (guint64) INFERIOR_REG_DS (arch->current_regs);
	values [DEBUGGER_REG_ES] = (guint64) INFERIOR_REG_ES (arch->current_regs);
	values [DEBUGGER_REG_FS] = (guint64) INFERIOR_REG_FS (arch->current_regs);
	values [DEBUGGER_REG_GS] = (guint64) INFERIOR_REG_GS (arch->current_regs);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_set_registers (ServerHandle *handle, guint64 *values)
{
	ArchInfo *arch = handle->arch;

	INFERIOR_REG_R15 (arch->current_regs) = values [DEBUGGER_REG_R15];
	INFERIOR_REG_R14 (arch->current_regs) = values [DEBUGGER_REG_R14];
	INFERIOR_REG_R13 (arch->current_regs) = values [DEBUGGER_REG_R13];
	INFERIOR_REG_R12 (arch->current_regs) = values [DEBUGGER_REG_R12];
	INFERIOR_REG_RBP (arch->current_regs) = values [DEBUGGER_REG_RBP];
	INFERIOR_REG_RBX (arch->current_regs) = values [DEBUGGER_REG_RBX];
	INFERIOR_REG_R11 (arch->current_regs) = values [DEBUGGER_REG_R11];
	INFERIOR_REG_R10 (arch->current_regs) = values [DEBUGGER_REG_R10];
	INFERIOR_REG_R9 (arch->current_regs) = values [DEBUGGER_REG_R9];
	INFERIOR_REG_R8 (arch->current_regs) = values [DEBUGGER_REG_R8];
	INFERIOR_REG_RAX (arch->current_regs) = values [DEBUGGER_REG_RAX];
	INFERIOR_REG_RCX (arch->current_regs) = values [DEBUGGER_REG_RCX];
	INFERIOR_REG_RDX (arch->current_regs) = values [DEBUGGER_REG_RDX];
	INFERIOR_REG_RSI (arch->current_regs) = values [DEBUGGER_REG_RSI];
	INFERIOR_REG_RDI (arch->current_regs) = values [DEBUGGER_REG_RDI];
	INFERIOR_REG_ORIG_RAX (arch->current_regs) = values [DEBUGGER_REG_ORIG_RAX];
	INFERIOR_REG_RIP (arch->current_regs) = values [DEBUGGER_REG_RIP];
	INFERIOR_REG_CS (arch->current_regs) = values [DEBUGGER_REG_CS];
	INFERIOR_REG_EFLAGS (arch->current_regs) = values [DEBUGGER_REG_EFLAGS];
	INFERIOR_REG_RSP (arch->current_regs) = values [DEBUGGER_REG_RSP];
	INFERIOR_REG_SS (arch->current_regs) = values [DEBUGGER_REG_SS];
	INFERIOR_REG_FS_BASE (arch->current_regs) = values [DEBUGGER_REG_FS_BASE];
	INFERIOR_REG_GS_BASE (arch->current_regs) = values [DEBUGGER_REG_GS_BASE];
	INFERIOR_REG_DS (arch->current_regs) = values [DEBUGGER_REG_DS];
	INFERIOR_REG_ES (arch->current_regs) = values [DEBUGGER_REG_ES];
	INFERIOR_REG_FS (arch->current_regs) = values [DEBUGGER_REG_FS];
	INFERIOR_REG_GS (arch->current_regs) = values [DEBUGGER_REG_GS];

	return _server_ptrace_set_registers (handle->inferior, &arch->current_regs);
}

static ServerCommandError
do_enable (ServerHandle *handle, X86BreakpointInfo *breakpoint)
{
	ServerCommandError result;
	char bopcode = 0xcc;
	guint64 address;

	if (breakpoint->info.enabled)
		return COMMAND_ERROR_NONE;

	address = (guint64) breakpoint->info.address;

	if (breakpoint->dr_index >= 0) {
		return COMMAND_ERROR_NOT_IMPLEMENTED;
	} else {
		result = server_ptrace_read_memory (handle, address, 1, &breakpoint->saved_insn);
		if (result != COMMAND_ERROR_NONE)
			return result;

		result = server_ptrace_write_memory (handle, address, 1, &bopcode);
		if (result != COMMAND_ERROR_NONE)
			return result;
	}

	breakpoint->info.enabled = TRUE;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
do_disable (ServerHandle *handle, X86BreakpointInfo *breakpoint)
{
	ServerCommandError result;
	guint64 address;

	if (!breakpoint->info.enabled)
		return COMMAND_ERROR_NONE;

	address = (guint64) breakpoint->info.address;

	if (breakpoint->dr_index >= 0) {
		return COMMAND_ERROR_NOT_IMPLEMENTED;
	} else {
		result = server_ptrace_write_memory (handle, address, 1, &breakpoint->saved_insn);
		if (result != COMMAND_ERROR_NONE)
			return result;
	}

	breakpoint->info.enabled = FALSE;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_insert_breakpoint (ServerHandle *handle, guint64 address, guint32 *bhandle)
{
	X86BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = (X86BreakpointInfo *) mono_debugger_breakpoint_manager_lookup (handle->bpm, address);
	if (breakpoint && !breakpoint->info.is_hardware_bpt) {
		breakpoint->info.refcount++;
		goto done;
	}

	breakpoint = g_new0 (X86BreakpointInfo, 1);

	breakpoint->info.refcount = 1;
	breakpoint->info.address = address;
	breakpoint->info.is_hardware_bpt = FALSE;
	breakpoint->info.id = mono_debugger_breakpoint_manager_get_next_id ();
	breakpoint->dr_index = -1;

	result = do_enable (handle, breakpoint);
	if (result != COMMAND_ERROR_NONE) {
		mono_debugger_breakpoint_manager_unlock ();
		g_free (breakpoint);
		return result;
	}

	mono_debugger_breakpoint_manager_insert (handle->bpm, (BreakpointInfo *) breakpoint);
 done:
	*bhandle = breakpoint->info.id;
	mono_debugger_breakpoint_manager_unlock ();

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_remove_breakpoint (ServerHandle *handle, guint32 bhandle)
{
	X86BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = (X86BreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (handle->bpm, bhandle);
	if (!breakpoint) {
		result = COMMAND_ERROR_NO_SUCH_BREAKPOINT;
		goto out;
	}

	if (--breakpoint->info.refcount > 0) {
		result = COMMAND_ERROR_NONE;
		goto out;
	}

	result = do_disable (handle, breakpoint);
	if (result != COMMAND_ERROR_NONE)
		goto out;

	mono_debugger_breakpoint_manager_remove (handle->bpm, (BreakpointInfo *) breakpoint);

 out:
	mono_debugger_breakpoint_manager_unlock ();
	return result;
}

static ServerCommandError
server_ptrace_insert_hw_breakpoint (ServerHandle *handle, guint32 *idx,
				    guint64 address, guint32 *bhandle)
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

static ServerCommandError
server_ptrace_enable_breakpoint (ServerHandle *handle, guint32 bhandle)
{
	X86BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = (X86BreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (handle->bpm, bhandle);
	if (!breakpoint) {
		mono_debugger_breakpoint_manager_unlock ();
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;
	}

	result = do_enable (handle, breakpoint);
	mono_debugger_breakpoint_manager_unlock ();
	return result;
}

static ServerCommandError
server_ptrace_disable_breakpoint (ServerHandle *handle, guint32 bhandle)
{
	X86BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = (X86BreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (handle->bpm, bhandle);
	if (!breakpoint) {
		mono_debugger_breakpoint_manager_unlock ();
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;
	}

	result = do_disable (handle, breakpoint);
	mono_debugger_breakpoint_manager_unlock ();
	return result;
}

static ServerCommandError
server_ptrace_get_breakpoints (ServerHandle *handle, guint32 *count, guint32 **retval)
{
	int i;
	GPtrArray *breakpoints;

	mono_debugger_breakpoint_manager_lock ();
	breakpoints = mono_debugger_breakpoint_manager_get_breakpoints (handle->bpm);
	*count = breakpoints->len;
	*retval = g_new0 (guint32, breakpoints->len);

	for (i = 0; i < breakpoints->len; i++) {
		BreakpointInfo *info = g_ptr_array_index (breakpoints, i);

		(*retval) [i] = info->id;
	}
	mono_debugger_breakpoint_manager_unlock ();

	return COMMAND_ERROR_NONE;	
}

static ServerCommandError
server_ptrace_call_method (ServerHandle *handle, guint64 method_address,
			   guint64 method_argument1, guint64 method_argument2,
			   guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	long new_rsp;

	guint8 code[] = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
			  0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
			  0xcc };
	int size = sizeof (code);

	if (arch->saved_regs)
		return COMMAND_ERROR_RECURSIVE_CALL;

	new_rsp = INFERIOR_REG_RSP (arch->current_regs) - size;

	*((guint64 *) code) = new_rsp + 16;
	*((guint64 *) (code+8)) = callback_argument;

	arch->saved_regs = g_memdup (&arch->current_regs, sizeof (arch->current_regs));
	arch->saved_fpregs = g_memdup (&arch->current_fpregs, sizeof (arch->current_fpregs));
	arch->call_address = new_rsp + 16;
	arch->callback_argument = callback_argument;

	server_ptrace_write_memory (handle, (unsigned long) new_rsp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_RIP (arch->current_regs) = method_address;
	INFERIOR_REG_RDI (arch->current_regs) = method_argument1;
	INFERIOR_REG_RSI (arch->current_regs) = method_argument2;
	INFERIOR_REG_RSP (arch->current_regs) = new_rsp;

	result = _server_ptrace_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return server_ptrace_continue (handle);
}

/*
 * This method is highly architecture and specific.
 * It will only work on the i386.
 */

static ServerCommandError
server_ptrace_call_method_1 (ServerHandle *handle, guint64 method_address,
			     guint64 method_argument, const gchar *string_argument,
			     guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	long new_rsp;

	static guint8 static_code[] = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0xcc };
	int static_size = sizeof (static_code);
	int size = static_size + strlen (string_argument) + 1;
	guint8 *code = g_malloc0 (size);
	memcpy (code, static_code, static_size);
	strcpy (code + static_size, string_argument);

	if (arch->saved_regs)
		return COMMAND_ERROR_RECURSIVE_CALL;

	new_rsp = INFERIOR_REG_RSP (arch->current_regs) - size;

	*((guint64 *) code) = new_rsp + 16;
	*((guint64 *) (code+8)) = callback_argument;

	arch->saved_regs = g_memdup (&arch->current_regs, sizeof (arch->current_regs));
	arch->saved_fpregs = g_memdup (&arch->current_fpregs, sizeof (arch->current_fpregs));
	arch->call_address = new_rsp + 16;
	arch->callback_argument = callback_argument;

	server_ptrace_write_memory (handle, (unsigned long) new_rsp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_RIP (arch->current_regs) = method_address;
	INFERIOR_REG_RDI (arch->current_regs) = method_argument;
	INFERIOR_REG_RSI (arch->current_regs) = new_rsp + static_size;
	INFERIOR_REG_RSP (arch->current_regs) = new_rsp;

	result = _server_ptrace_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return server_ptrace_continue (handle);
}

static ServerCommandError
server_ptrace_call_method_invoke (ServerHandle *handle, guint64 invoke_method,
				  guint64 method_argument, guint32 num_params,
				  guint32 blob_size, guint64 *param_data,
				  gint32 *offset_data, gconstpointer blob_data,
				  guint64 callback_argument, gboolean debug)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	RuntimeInvokeData *rdata;
	guint64 new_rsp;
	int i;

	static guint8 static_code[] = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0xcc };
	int static_size = sizeof (static_code);
	int size = static_size + (num_params + 3) * 8 + blob_size;
	guint8 *code = g_malloc0 (size);
	guint64 *ptr = (guint64 *) (code + static_size + blob_size);
	guint64 blob_start;
	memcpy (code, static_code, static_size);
	memcpy (code + static_size, blob_data, blob_size);

	if (arch->saved_regs)
		return COMMAND_ERROR_RECURSIVE_CALL;

	new_rsp = INFERIOR_REG_RSP (arch->current_regs) - size;
	blob_start = new_rsp + static_size;

	for (i = 0; i < num_params; i++) {
		if (offset_data [i] >= 0)
			ptr [i] = blob_start + offset_data [i];
		else
			ptr [i] = param_data [i];
	}

	*((guint64 *) code) = new_rsp + static_size - 1;
	*((guint64 *) (code+8)) = callback_argument;

	rdata = g_new0 (RuntimeInvokeData, 1);
	rdata->saved_regs = g_memdup (&arch->current_regs, sizeof (arch->current_regs));
	rdata->saved_fpregs = g_memdup (&arch->current_fpregs, sizeof (arch->current_fpregs));
	rdata->call_address = new_rsp + static_size - 1;
	rdata->exc_address = new_rsp + 16;
	rdata->callback_argument = callback_argument;
	rdata->debug = debug;

	server_ptrace_write_memory (handle, (unsigned long) new_rsp, size, code);
	g_free (code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_RIP (arch->current_regs) = invoke_method;
	INFERIOR_REG_RDI (arch->current_regs) = method_argument;
	INFERIOR_REG_RSI (arch->current_regs) = ptr [0];
	INFERIOR_REG_RDX (arch->current_regs) = new_rsp + static_size + blob_size + 8;
	INFERIOR_REG_RCX (arch->current_regs) = new_rsp + 16;
	INFERIOR_REG_RSP (arch->current_regs) = new_rsp;

	g_ptr_array_add (arch->rti_stack, rdata);

	result = _server_ptrace_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return server_ptrace_continue (handle);
}
