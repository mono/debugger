#define _GNU_SOURCE
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

#include "powerpc-arch.h"

struct ArchInfo
{
	INFERIOR_REGS_TYPE current_regs;
	INFERIOR_REGS_TYPE *saved_regs;
};

ArchInfo *
powerpc_arch_initialize (void)
{
	ArchInfo *arch = g_new0 (ArchInfo, 1);

	return arch;
}

void
powerpc_arch_finalize (ArchInfo *arch)
{
	g_free (arch->saved_regs);
	g_free (arch);
}

static ServerCommandError
powerpc_arch_get_registers (ServerHandle *handle)
{
	ServerCommandError result;

	result = _powerpc_get_registers (handle->inferior, &handle->arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
powerpc_get_registers (ServerHandle *handle, guint32 count,
		       guint32 *registers, guint64 *values)
{
	ArchInfo *arch = handle->arch;
	int i;

	for (i = 0; i < count; i++) {
		switch (registers [i]) {
		case DEBUGGER_REG_R0:
			values [i] = (guint32) INFERIOR_REG_R0 (arch->current_regs);
			break;
		case DEBUGGER_REG_R1:
			values [i] = (guint32) INFERIOR_REG_R1 (arch->current_regs);
			break;
		case DEBUGGER_REG_R2:
			values [i] = (guint32) INFERIOR_REG_R2 (arch->current_regs);
			break;
		case DEBUGGER_REG_R3:
			values [i] = (guint32) INFERIOR_REG_R3 (arch->current_regs);
			break;
		case DEBUGGER_REG_R4:
			values [i] = (guint32) INFERIOR_REG_R4 (arch->current_regs);
			break;
		case DEBUGGER_REG_R5:
			values [i] = (guint32) INFERIOR_REG_R5 (arch->current_regs);
			break;
		case DEBUGGER_REG_R6:
			values [i] = (guint32) INFERIOR_REG_R6 (arch->current_regs);
			break;
		case DEBUGGER_REG_R7:
			values [i] = (guint32) INFERIOR_REG_R7 (arch->current_regs);
			break;
		case DEBUGGER_REG_R8:
			values [i] = (guint32) INFERIOR_REG_R8 (arch->current_regs);
			break;
		case DEBUGGER_REG_R9:
			values [i] = (guint32) INFERIOR_REG_R9 (arch->current_regs);
			break;
		case DEBUGGER_REG_R10:
			values [i] = (guint32) INFERIOR_REG_R10 (arch->current_regs);
			break;
		case DEBUGGER_REG_R11:
			values [i] = (guint32) INFERIOR_REG_R11 (arch->current_regs);
			break;
		case DEBUGGER_REG_R12:
			values [i] = (guint32) INFERIOR_REG_R12 (arch->current_regs);
			break;
		case DEBUGGER_REG_R13:
			values [i] = (guint32) INFERIOR_REG_R13 (arch->current_regs);
			break;
		case DEBUGGER_REG_R14:
			values [i] = (guint32) INFERIOR_REG_R14 (arch->current_regs);
			break;
		case DEBUGGER_REG_R15:
			values [i] = (guint32) INFERIOR_REG_R15 (arch->current_regs);
			break;
		case DEBUGGER_REG_R16:
			values [i] = (guint32) INFERIOR_REG_R16 (arch->current_regs);
			break;
		case DEBUGGER_REG_R17:
			values [i] = (guint32) INFERIOR_REG_R17 (arch->current_regs);
			break;
		case DEBUGGER_REG_R18:
			values [i] = (guint32) INFERIOR_REG_R18 (arch->current_regs);
			break;
		case DEBUGGER_REG_R19:
			values [i] = (guint32) INFERIOR_REG_R19 (arch->current_regs);
			break;
		case DEBUGGER_REG_R20:
			values [i] = (guint32) INFERIOR_REG_R20 (arch->current_regs);
			break;
		case DEBUGGER_REG_R21:
			values [i] = (guint32) INFERIOR_REG_R21 (arch->current_regs);
			break;
		case DEBUGGER_REG_R22:
			values [i] = (guint32) INFERIOR_REG_R22 (arch->current_regs);
			break;
		case DEBUGGER_REG_R23:
			values [i] = (guint32) INFERIOR_REG_R23 (arch->current_regs);
			break;
		case DEBUGGER_REG_R24:
			values [i] = (guint32) INFERIOR_REG_R24 (arch->current_regs);
			break;
		case DEBUGGER_REG_R25:
			values [i] = (guint32) INFERIOR_REG_R25 (arch->current_regs);
			break;
		case DEBUGGER_REG_R26:
			values [i] = (guint32) INFERIOR_REG_R26 (arch->current_regs);
			break;
		case DEBUGGER_REG_R27:
			values [i] = (guint32) INFERIOR_REG_R27 (arch->current_regs);
			break;
		case DEBUGGER_REG_R28:
			values [i] = (guint32) INFERIOR_REG_R28 (arch->current_regs);
			break;
		case DEBUGGER_REG_R29:
			values [i] = (guint32) INFERIOR_REG_R29 (arch->current_regs);
			break;
		case DEBUGGER_REG_R30:
			values [i] = (guint32) INFERIOR_REG_R30 (arch->current_regs);
			break;
		case DEBUGGER_REG_R31:
			values [i] = (guint32) INFERIOR_REG_R31 (arch->current_regs);
			break;
		case DEBUGGER_REG_PC:
			values [i] = (guint32) INFERIOR_REG_PC (arch->current_regs);
			break;
		case DEBUGGER_REG_PS:
			values [i] = (guint32) INFERIOR_REG_PS (arch->current_regs);
			break;
		case DEBUGGER_REG_CR:
			values [i] = (guint32) INFERIOR_REG_CR (arch->current_regs);
			break;
		case DEBUGGER_REG_LR:
			values [i] = (guint32) INFERIOR_REG_LR (arch->current_regs);
			break;
		case DEBUGGER_REG_CTR:
			values [i] = (guint32) INFERIOR_REG_CTR (arch->current_regs);
			break;
		case DEBUGGER_REG_XER:
			values [i] = (guint32) INFERIOR_REG_XER (arch->current_regs);
			break;
		case DEBUGGER_REG_MQ:
			values [i] = (guint32) INFERIOR_REG_MQ (arch->current_regs);
			break;
		case DEBUGGER_REG_VRSAVE:
			values [i] = (guint32) INFERIOR_REG_VRSAVE (arch->current_regs);
			break;
		default:
			return COMMAND_ERROR_UNKNOWN_REGISTER;
		}
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
powerpc_set_registers (ServerHandle *handle, guint32 count,
		       guint32 *registers, guint64 *values)
{
	ArchInfo *arch = handle->arch;
	int i;

	for (i = 0; i < count; i++) {
		switch (registers [i]) {
		case DEBUGGER_REG_R0:
			INFERIOR_REG_R0 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R1:
			INFERIOR_REG_R1 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R2:
			INFERIOR_REG_R2 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R3:
			INFERIOR_REG_R3 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R4:
			INFERIOR_REG_R4 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R5:
			INFERIOR_REG_R5 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R6:
			INFERIOR_REG_R6 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R7:
			INFERIOR_REG_R7 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R8:
			INFERIOR_REG_R8 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R9:
			INFERIOR_REG_R9 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R10:
			INFERIOR_REG_R10 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R11:
			INFERIOR_REG_R11 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R12:
			INFERIOR_REG_R12 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R13:
			INFERIOR_REG_R13 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R14:
			INFERIOR_REG_R14 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R15:
			INFERIOR_REG_R15 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R16:
			INFERIOR_REG_R16 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R17:
			INFERIOR_REG_R17 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R18:
			INFERIOR_REG_R18 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R19:
			INFERIOR_REG_R19 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R20:
			INFERIOR_REG_R20 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R21:
			INFERIOR_REG_R21 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R22:
			INFERIOR_REG_R22 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R23:
			INFERIOR_REG_R23 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R24:
			INFERIOR_REG_R24 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R25:
			INFERIOR_REG_R25 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R26:
			INFERIOR_REG_R26 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R27:
			INFERIOR_REG_R27 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R28:
			INFERIOR_REG_R28 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R29:
			INFERIOR_REG_R29 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R30:
			INFERIOR_REG_R30 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_R31:
			INFERIOR_REG_R31 (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_PC:
			INFERIOR_REG_PC (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_PS:
			INFERIOR_REG_PS (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_CR:
			INFERIOR_REG_CR (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_LR:
			INFERIOR_REG_LR (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_CTR:
			INFERIOR_REG_CTR (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_XER:
			INFERIOR_REG_XER (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_MQ:
			INFERIOR_REG_MQ (arch->current_regs) = values [i];
			break;
		case DEBUGGER_REG_VRSAVE:
			INFERIOR_REG_VRSAVE (arch->current_regs) = values [i];
			break;
		default:
			return COMMAND_ERROR_UNKNOWN_REGISTER;
		}
	}

	return _powerpc_set_registers (handle->inferior, &arch->current_regs);
}

static ServerCommandError
powerpc_get_pc (ServerHandle *handle, guint64 *pc)
{
	*pc = (guint32) INFERIOR_REG_PC (handle->arch->current_regs);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
powerpc_get_ret_address (ServerHandle *handle, guint64 *retval)
{
	*retval = 0;
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
do_enable (ServerHandle *handle, PowerPcBreakpointInfo *breakpoint)
{
	ServerCommandError result;
	char bopcode[4] = { 0x7f, 0xe0, 0x00, 0x08 };
	guint32 address;

	if (breakpoint->info.enabled)
		return COMMAND_ERROR_NONE;

	address = (guint32) breakpoint->info.address;

	result = powerpc_read_memory (handle, address, 4, &breakpoint->saved_insn);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = powerpc_write_memory (handle, address, 4, &bopcode);
	if (result != COMMAND_ERROR_NONE)
		return result;

	breakpoint->info.enabled = TRUE;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
do_disable (ServerHandle *handle, PowerPcBreakpointInfo *breakpoint)
{
	ServerCommandError result;
	guint32 address;

	if (!breakpoint->info.enabled)
		return COMMAND_ERROR_NONE;

	address = (guint32) breakpoint->info.address;

	result = powerpc_write_memory (handle, address, 4, &breakpoint->saved_insn);
	if (result != COMMAND_ERROR_NONE)
		return result;

	breakpoint->info.enabled = FALSE;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
powerpc_insert_breakpoint (ServerHandle *handle, guint64 address, guint32 *bhandle)
{
	PowerPcBreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock (handle->bpm);
	breakpoint = (PowerPcBreakpointInfo *) mono_debugger_breakpoint_manager_lookup (handle->bpm, address);
	if (breakpoint && !breakpoint->info.is_hardware_bpt) {
		breakpoint->info.refcount++;
		goto done;
	}

	breakpoint = g_new0 (PowerPcBreakpointInfo, 1);

	breakpoint->info.refcount = 1;
	breakpoint->info.address = address;
	breakpoint->info.is_hardware_bpt = FALSE;
	breakpoint->info.id = mono_debugger_breakpoint_manager_get_next_id ();

	result = do_enable (handle, breakpoint);
	if (result != COMMAND_ERROR_NONE) {
		mono_debugger_breakpoint_manager_unlock (handle->bpm);
		g_free (breakpoint);
		return result;
	}

	mono_debugger_breakpoint_manager_insert (handle->bpm, (BreakpointInfo *) breakpoint);
 done:
	*bhandle = breakpoint->info.id;
	mono_debugger_breakpoint_manager_unlock (handle->bpm);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
powerpc_remove_breakpoint (ServerHandle *handle, guint32 bhandle)
{
	PowerPcBreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock (handle->bpm);
	breakpoint = (PowerPcBreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (handle->bpm, bhandle);
	if (!breakpoint) {
		result = COMMAND_ERROR_NO_SUCH_BREAKPOINT;
		goto out;
	}

	result = do_disable (handle, breakpoint);
	if (result != COMMAND_ERROR_NONE)
		goto out;

	mono_debugger_breakpoint_manager_remove (handle->bpm, (BreakpointInfo *) breakpoint);

 out:
	mono_debugger_breakpoint_manager_unlock (handle->bpm);
	return result;
}

static ServerCommandError
powerpc_enable_breakpoint (ServerHandle *handle, guint32 bhandle)
{
	PowerPcBreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock (handle->bpm);
	breakpoint = (PowerPcBreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (handle->bpm, bhandle);
	if (!breakpoint) {
		mono_debugger_breakpoint_manager_unlock (handle->bpm);
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;
	}

	result = do_enable (handle, breakpoint);
	mono_debugger_breakpoint_manager_unlock (handle->bpm);
	return result;
}

static ServerCommandError
powerpc_disable_breakpoint (ServerHandle *handle, guint32 bhandle)
{
	PowerPcBreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock (handle->bpm);
	breakpoint = (PowerPcBreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (handle->bpm, bhandle);
	if (!breakpoint) {
		mono_debugger_breakpoint_manager_unlock (handle->bpm);
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;
	}

	result = do_disable (handle, breakpoint);
	mono_debugger_breakpoint_manager_unlock (handle->bpm);
	return result;
}

static ServerCommandError
powerpc_get_breakpoints (ServerHandle *handle, guint32 *count, guint32 **retval)
{
	int i;
	GPtrArray *breakpoints;

	mono_debugger_breakpoint_manager_lock (handle->bpm);
	breakpoints = mono_debugger_breakpoint_manager_get_breakpoints (handle->bpm);
	*count = breakpoints->len;
	*retval = g_new0 (guint32, breakpoints->len);

	for (i = 0; i < breakpoints->len; i++) {
		BreakpointInfo *info = g_ptr_array_index (breakpoints, i);

		(*retval) [i] = info->id;
	}
	mono_debugger_breakpoint_manager_unlock (handle->bpm);

	return COMMAND_ERROR_NONE;	
}

static ServerCommandError
powerpc_current_insn_is_bpt (ServerHandle *handle, guint32 *is_breakpoint)
{
	mono_debugger_breakpoint_manager_lock (handle->bpm);
	if (mono_debugger_breakpoint_manager_lookup (handle->bpm, INFERIOR_REG_PC (handle->arch->current_regs)))
		*is_breakpoint = TRUE;
	else
		*is_breakpoint = FALSE;
	mono_debugger_breakpoint_manager_unlock (handle->bpm);

	return COMMAND_ERROR_NONE;
}

static gboolean
check_breakpoint (ServerHandle *handle, guint64 address, guint64 *retval)
{
	PowerPcBreakpointInfo *info;

	mono_debugger_breakpoint_manager_lock (handle->bpm);
	info = (PowerPcBreakpointInfo *) mono_debugger_breakpoint_manager_lookup (handle->bpm, address);
	if (!info || !info->info.enabled) {
		mono_debugger_breakpoint_manager_unlock (handle->bpm);
		return FALSE;
	}

	*retval = info->info.id;
	mono_debugger_breakpoint_manager_unlock (handle->bpm);
	return TRUE;
}

static ChildStoppedAction
powerpc_arch_child_stopped (ServerHandle *handle, int stopsig,
			    guint64 *callback_arg, guint64 *retval, guint64 *retval2)
{
	ArchInfo *arch = handle->arch;

	powerpc_arch_get_registers (handle);

	if (check_breakpoint (handle, INFERIOR_REG_PC (arch->current_regs), retval))
		return STOP_ACTION_BREAKPOINT_HIT;


	return STOP_ACTION_SEND_STOPPED;
}

static ServerCommandError
powerpc_get_backtrace (ServerHandle *handle, gint32 max_frames,
		       guint64 stop_address, guint32 *count, StackFrame **data)
{
	GArray *frames = g_array_new (FALSE, FALSE, sizeof (StackFrame));
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	StackFrame sframe;
	int i;

	sframe.address = (guint32) INFERIOR_REG_PC (arch->current_regs);
	sframe.frame_address = (guint32) INFERIOR_REG_R30 (arch->current_regs);

	g_array_append_val (frames, sframe);
	goto out;


 out:
	*count = frames->len;
	*data = g_new0 (StackFrame, frames->len);
	for (i = 0; i < frames->len; i++)
		(*data)[i] = g_array_index (frames, StackFrame, i);
	g_array_free (frames, FALSE);
	return result;
}
