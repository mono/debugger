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

#include "x86-arch.h"

typedef struct
{
	int slot;
	int insn_size;
	gboolean update_ip;
	guint32 code_address;
	guint32 original_eip;
} CodeBufferData;

struct ArchInfo
{
	INFERIOR_REGS_TYPE current_regs;
	INFERIOR_FPREGS_TYPE current_fpregs;
	GPtrArray *callback_stack;
	CodeBufferData *code_buffer;
	guint64 dr_control, dr_status;
	int dr_regs [DR_NADDR];
};

typedef struct
{
	INFERIOR_REGS_TYPE saved_regs;
	INFERIOR_FPREGS_TYPE saved_fpregs;
	guint64 callback_argument;
	guint32 call_address;
	guint32 stack_pointer;
	guint32 rti_frame;
	guint32 exc_address;
	guint32 pushed_registers;
	guint32 data_pointer;
	guint32 data_size;
	int saved_signal;
	gboolean debug;
	gboolean is_rti;
} CallbackData;

ArchInfo *
x86_arch_initialize (void)
{
	ArchInfo *arch = g_new0 (ArchInfo, 1);

	arch->callback_stack = g_ptr_array_new ();

	return arch;
}

void
x86_arch_finalize (ArchInfo *arch)
{
	g_ptr_array_free (arch->callback_stack, TRUE);
	g_free (arch);
}

static ServerCommandError
server_ptrace_current_insn_is_bpt (ServerHandle *handle, guint32 *is_breakpoint)
{
	mono_debugger_breakpoint_manager_lock ();
	if (mono_debugger_breakpoint_manager_lookup (handle->bpm, INFERIOR_REG_EIP (handle->arch->current_regs)))
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
		BreakpointInfo *info = g_ptr_array_index (breakpoints, i);
		guint32 offset;

		if (info->is_hardware_bpt || !info->enabled)
			continue;
		if ((info->address < start) || (info->address >= start+size))
			continue;

		offset = (guint32) info->address - start;
		ptr [offset] = info->saved_insn;
	}

	mono_debugger_breakpoint_manager_unlock ();
}

static ServerCommandError
server_ptrace_get_frame (ServerHandle *handle, StackFrame *frame)
{
	ServerCommandError result;

	result = x86_arch_get_registers (handle);
	if (result != COMMAND_ERROR_NONE)
		return result;

	frame->address = (guint32) INFERIOR_REG_EIP (handle->arch->current_regs);
	frame->stack_pointer = (guint32) INFERIOR_REG_ESP (handle->arch->current_regs);
	frame->frame_address = (guint32) INFERIOR_REG_EBP (handle->arch->current_regs);
	return COMMAND_ERROR_NONE;
}

/*
 * This method is highly architecture and specific.
 * It will only work on the i386.
 */

static ServerCommandError
server_ptrace_call_method (ServerHandle *handle, guint64 method_address,
			   guint64 method_argument1, guint64 method_argument2,
			   guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	guint32 new_esp, call_disp;
	CallbackData *cdata;

	guint8 code[] = { 0x68, 0x00, 0x00, 0x00, 0x00, 0x68, 0x00, 0x00,
			  0x00, 0x00, 0x68, 0x00, 0x00, 0x00, 0x00, 0x68,
			  0x00, 0x00, 0x00, 0x00, 0xe8, 0x00, 0x00, 0x00,
			  0x00, 0xcc };
	int size = sizeof (code);

	cdata = g_new0 (CallbackData, 1);

	new_esp = (guint32) INFERIOR_REG_ESP (arch->current_regs) - size;

	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	memcpy (&cdata->saved_fpregs, &arch->current_fpregs, sizeof (arch->current_fpregs));
	cdata->call_address = new_esp + 26;
	cdata->stack_pointer = new_esp - 16;
	cdata->callback_argument = callback_argument;
	cdata->saved_signal = handle->inferior->last_signal;
	handle->inferior->last_signal = 0;

	call_disp = (int) method_address - new_esp;

	*((guint32 *) (code+1)) = method_argument2 >> 32;
	*((guint32 *) (code+6)) = method_argument2 & 0xffffffff;
	*((guint32 *) (code+11)) = method_argument1 >> 32;
	*((guint32 *) (code+16)) = method_argument1 & 0xffffffff;
	*((guint32 *) (code+21)) = call_disp - 25;

	result = server_ptrace_write_memory (handle, (guint32) new_esp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = _server_ptrace_make_memory_executable (handle, (guint32) new_esp, size);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_ORIG_EAX (arch->current_regs) = -1;
	INFERIOR_REG_ESP (arch->current_regs) = INFERIOR_REG_EIP (arch->current_regs) = new_esp;

	g_ptr_array_add (arch->callback_stack, cdata);

	result = _server_ptrace_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

#if __MACH__
	handle->inferior->os.is_stopped = TRUE;
#endif
	return server_ptrace_continue (handle);
}

/*
 * This method is highly architecture and specific.
 * It will only work on the i386.
 */

static ServerCommandError
server_ptrace_call_method_1 (ServerHandle *handle, guint64 method_address,
			     guint64 method_argument, guint64 data_argument,
			     guint64 data_argument2, const gchar *string_argument,
			     guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	guint32 new_esp, call_disp;
	CallbackData *cdata;

	static guint8 static_code[] = { 0x68, 0x00, 0x00, 0x00, 0x00, 0x68, 0x00, 0x00,
					0x00, 0x00, 0x68, 0x00, 0x00, 0x00, 0x00, 0x68,
					0x00, 0x00, 0x00, 0x00, 0x68, 0x00, 0x00, 0x00,
					0x00, 0x68, 0x00, 0x00, 0x00, 0x00, 0x68, 0x00,
					0x00, 0x00, 0x00, 0xe8, 0x00, 0x00, 0x00, 0x00,
					0xcc };
	int static_size = sizeof (static_code);
	int size = static_size + strlen (string_argument) + 1;
	guint8 *code = g_malloc0 (size);
	memcpy (code, static_code, static_size);
	strcpy ((char *) (code + static_size), string_argument);

	cdata = g_new0 (CallbackData, 1);

	new_esp = (guint32) INFERIOR_REG_ESP (arch->current_regs) - size;

	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	memcpy (&cdata->saved_fpregs, &arch->current_fpregs, sizeof (arch->current_fpregs));
	cdata->call_address = new_esp + static_size;
	cdata->stack_pointer = new_esp - 28;
	cdata->callback_argument = callback_argument;
	cdata->saved_signal = handle->inferior->last_signal;
	handle->inferior->last_signal = 0;

	call_disp = (int) method_address - new_esp;

	*((guint32 *) (code+1)) = new_esp + static_size;
	*((guint32 *) (code+6)) = data_argument2 >> 32;
	*((guint32 *) (code+11)) = data_argument2 & 0xffffffff;
	*((guint32 *) (code+16)) = data_argument >> 32;
	*((guint32 *) (code+21)) = data_argument & 0xffffffff;
	*((guint32 *) (code+26)) = method_argument >> 32;
	*((guint32 *) (code+31)) = method_argument & 0xffffffff;
	*((guint32 *) (code+36)) = call_disp - 40;

	result = server_ptrace_write_memory (handle, (guint32) new_esp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = _server_ptrace_make_memory_executable (handle, (guint32) new_esp, size);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_ORIG_EAX (arch->current_regs) = -1;
	INFERIOR_REG_ESP (arch->current_regs) = INFERIOR_REG_EIP (arch->current_regs) = new_esp;

	g_ptr_array_add (arch->callback_stack, cdata);

	result = _server_ptrace_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

#if __MACH__
	handle->inferior->os.is_stopped = TRUE;
#endif
	return server_ptrace_continue (handle);
}

static ServerCommandError
server_ptrace_call_method_2 (ServerHandle *handle, guint64 method_address,
			     guint32 data_size, gconstpointer data_buffer,
			     guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	CallbackData *cdata;
	guint32 new_esp;

	int size = 57 + data_size;
	guint8 *code = g_malloc0 (size);

	new_esp = INFERIOR_REG_ESP (arch->current_regs) - size;

	*((guint32 *) code) = new_esp + size - 1;
	*((guint64 *) (code+4)) = new_esp + 20;
	*((guint64 *) (code+12)) = new_esp + 56;
	*((guint32 *) (code+20)) = INFERIOR_REG_EAX (arch->current_regs);
	*((guint32 *) (code+24)) = INFERIOR_REG_EBX (arch->current_regs);
	*((guint32 *) (code+28)) = INFERIOR_REG_ECX (arch->current_regs);
	*((guint32 *) (code+32)) = INFERIOR_REG_EDX (arch->current_regs);
	*((guint32 *) (code+36)) = INFERIOR_REG_EBP (arch->current_regs);
	*((guint32 *) (code+40)) = INFERIOR_REG_ESP (arch->current_regs);
	*((guint32 *) (code+44)) = INFERIOR_REG_ESI (arch->current_regs);
	*((guint32 *) (code+48)) = INFERIOR_REG_EDI (arch->current_regs);
	*((guint32 *) (code+52)) = INFERIOR_REG_EIP (arch->current_regs);
	*((guint8 *) (code+data_size+56)) = 0xcc;

	cdata = g_new0 (CallbackData, 1);
	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	memcpy (&cdata->saved_fpregs, &arch->current_fpregs, sizeof (arch->current_fpregs));
	cdata->call_address = new_esp + size;
	cdata->stack_pointer = new_esp + 4;
	cdata->exc_address = 0;
	cdata->callback_argument = callback_argument;
	cdata->saved_signal = handle->inferior->last_signal;
	cdata->pushed_registers = new_esp + 4;
	handle->inferior->last_signal = 0;

	if (data_size > 0) {
		memcpy (code+56, data_buffer, data_size);
		cdata->data_pointer = new_esp + 56;
		cdata->data_size = data_size;
	}

	result = server_ptrace_write_memory (handle, (guint32) new_esp, size, code);
	g_free (code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = _server_ptrace_make_memory_executable (handle, (guint32) new_esp, size);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_ORIG_EAX (arch->current_regs) = -1;
	INFERIOR_REG_EIP (arch->current_regs) = method_address;
	INFERIOR_REG_ESP (arch->current_regs) = new_esp;

	g_ptr_array_add (arch->callback_stack, cdata);

	result = _server_ptrace_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

#if __MACH__
	handle->inferior->os.is_stopped = TRUE;
#endif
	return server_ptrace_continue (handle);
}

static ServerCommandError
server_ptrace_call_method_3 (ServerHandle *handle, guint64 method_address,
			     guint64 method_argument, guint64 address_argument,
			     guint32 blob_size, gconstpointer blob_data,
			     guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	ArchInfo *arch = handle->arch;
	CallbackData *cdata;
	guint32 new_esp;
	int i;

	static guint8 static_code[] = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0xcc };
	int static_size = sizeof (static_code);
	int size = static_size + blob_size;
	guint8 *code = g_malloc0 (size);
	guint32 *ptr = (guint32 *) (code + static_size + blob_size);
	guint32 effective_address;
	guint32 blob_start;
	memcpy (code, static_code, static_size);
	memcpy (code + static_size, blob_data, blob_size);

	new_esp = INFERIOR_REG_ESP (arch->current_regs) - size;

	blob_start = new_esp + static_size;
	effective_address = address_argument ? address_argument : blob_start;

	*((guint32 *) code) = new_esp + static_size - 1;
	*((guint32 *) (code+4)) = method_argument;
	*((guint32 *) (code+12)) = effective_address;
	*((guint32 *) (code+20)) = new_esp;

	cdata = g_new0 (CallbackData, 1);
	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	memcpy (&cdata->saved_fpregs, &arch->current_fpregs, sizeof (arch->current_fpregs));
	cdata->call_address = new_esp + static_size;
	cdata->stack_pointer = new_esp + 4;
	cdata->callback_argument = callback_argument;
	cdata->saved_signal = handle->inferior->last_signal;
	handle->inferior->last_signal = 0;

	result = server_ptrace_write_memory (handle, (unsigned long) new_esp, size, code);
	g_free (code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = _server_ptrace_make_memory_executable (handle, (guint32) new_esp, size);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_ORIG_EAX (arch->current_regs) = -1;
	INFERIOR_REG_EIP (arch->current_regs) = method_address;
	INFERIOR_REG_ESP (arch->current_regs) = new_esp;
	INFERIOR_REG_EBP (arch->current_regs) = 0;

	g_ptr_array_add (arch->callback_stack, cdata);

	result = _server_ptrace_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

#if __MACH__
	handle->inferior->os.is_stopped = TRUE;
#endif
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
	CallbackData *cdata;
	guint32 new_esp;
	int i;

	static guint8 static_code[] = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
					0xcc };
	int static_size = sizeof (static_code);
	int size = static_size + (num_params + 3) * 4 + blob_size;
	guint8 *code = g_malloc0 (size);
	guint32 *ptr = (guint32 *) (code + static_size + blob_size);
	guint64 blob_start;

	memcpy (code, static_code, static_size);
	memcpy (code + static_size, blob_data, blob_size);

	new_esp = (guint32) INFERIOR_REG_ESP (arch->current_regs) - size;
	blob_start = new_esp + static_size;

	for (i = 0; i < num_params; i++) {
		if (offset_data [i] >= 0)
			ptr [i] = blob_start + offset_data [i];
		else
			ptr [i] = param_data [i];
	}

	*((guint32 *) code) = new_esp + static_size - 1;
	*((guint32 *) (code+4)) = method_argument;
	*((guint32 *) (code+8)) = ptr [0];
	*((guint32 *) (code+12)) = new_esp + static_size + blob_size + 4;
	*((guint32 *) (code+16)) = new_esp + 20;

	cdata = g_new0 (CallbackData, 1);
	memcpy (&cdata->saved_regs, &arch->current_regs, sizeof (arch->current_regs));
	memcpy (&cdata->saved_fpregs, &arch->current_fpregs, sizeof (arch->current_fpregs));
	cdata->call_address = new_esp + static_size;
	cdata->stack_pointer = new_esp + 4;
	cdata->exc_address = new_esp + 20;
	cdata->callback_argument = callback_argument;
	cdata->debug = debug;
	cdata->is_rti = TRUE;
	cdata->saved_signal = handle->inferior->last_signal;
	handle->inferior->last_signal = 0;

	result = server_ptrace_write_memory (handle, (guint32) new_esp, size, code);
	g_free (code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = _server_ptrace_make_memory_executable (handle, (guint32) new_esp, size);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_ORIG_EAX (arch->current_regs) = -1;
	INFERIOR_REG_EIP (arch->current_regs) = invoke_method;
	INFERIOR_REG_ESP (arch->current_regs) = new_esp;
	INFERIOR_REG_EBP (arch->current_regs) = 0;

	g_ptr_array_add (arch->callback_stack, cdata);

	result = _server_ptrace_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

#if __MACH__
	handle->inferior->os.is_stopped = TRUE;
#endif
	return server_ptrace_continue (handle);
}

static gboolean
check_breakpoint (ServerHandle *handle, guint64 address, guint64 *retval)
{
	BreakpointInfo *info;

	mono_debugger_breakpoint_manager_lock ();
	info = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup (handle->bpm, address);
	if (!info || !info->enabled) {
		mono_debugger_breakpoint_manager_unlock ();
		return FALSE;
	}

	*retval = info->id;
	mono_debugger_breakpoint_manager_unlock ();
	return TRUE;
}

static CallbackData *
get_callback_data (ArchInfo *arch)
{
	if (!arch->callback_stack->len)
		return NULL;

	return g_ptr_array_index (arch->callback_stack, arch->callback_stack->len - 1);
}

static ServerCommandError
x86_arch_get_registers (ServerHandle *handle)
{
	ServerCommandError result;

	result = _server_ptrace_get_registers (handle->inferior, &handle->arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = _server_ptrace_get_fp_registers (handle->inferior, &handle->arch->current_fpregs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = _server_ptrace_get_dr (handle->inferior, DR_STATUS, &handle->arch->dr_status);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}

ChildStoppedAction
x86_arch_child_stopped (ServerHandle *handle, int stopsig,
			guint64 *callback_arg, guint64 *retval, guint64 *retval2,
			guint32 *opt_data_size, gpointer *opt_data)
{
	ArchInfo *arch = handle->arch;
	InferiorHandle *inferior = handle->inferior;
	CodeBufferData *cbuffer = NULL;
	CallbackData *cdata;
	guint64 code;
	int i;

	x86_arch_get_registers (handle);

	if (stopsig == SIGSTOP)
		return STOP_ACTION_INTERRUPTED;

#if defined(__linux__) || defined(__FreeBSD__)
	if (stopsig != SIGTRAP)
		return STOP_ACTION_STOPPED;
#endif

	if (handle->mono_runtime &&
	    ((guint32) INFERIOR_REG_EIP (arch->current_regs) - 1 == handle->mono_runtime->notification_address)) {
		guint32 addr = (guint32) INFERIOR_REG_ESP (arch->current_regs) + 4;
		guint64 data [3];

		if (stopsig != SIGTRAP)
			return STOP_ACTION_STOPPED;

		if (server_ptrace_read_memory (handle, addr, 24, &data))
			return STOP_ACTION_STOPPED;

		*callback_arg = data [0];
		*retval = data [1];
		*retval2 = data [2];

		return STOP_ACTION_NOTIFICATION;
	}

	for (i = 0; i < DR_NADDR; i++) {
		if (X86_DR_WATCH_HIT (arch, i)) {
			_server_ptrace_set_dr (inferior, DR_STATUS, 0);
			arch->dr_status = 0;
			*retval = arch->dr_regs [i];
			return STOP_ACTION_BREAKPOINT_HIT;
		}
	}

	if (check_breakpoint (handle, (guint32) INFERIOR_REG_EIP (arch->current_regs) - 1, retval)) {
		INFERIOR_REG_EIP (arch->current_regs)--;
		_server_ptrace_set_registers (inferior, &arch->current_regs);
		return STOP_ACTION_BREAKPOINT_HIT;
	}

	cdata = get_callback_data (arch);
	if (cdata && (cdata->call_address == INFERIOR_REG_EIP (arch->current_regs))) {
		guint64 exc_object;

		if (cdata->pushed_registers) {
			guint32 pushed_regs [9];

			if (_server_ptrace_read_memory (handle, cdata->pushed_registers, 36, &pushed_regs))
				g_error (G_STRLOC ": Can't restore registers after returning from a call");

			INFERIOR_REG_EAX (cdata->saved_regs) = pushed_regs [0];
			INFERIOR_REG_EBX (cdata->saved_regs) = pushed_regs [1];
			INFERIOR_REG_ECX (cdata->saved_regs) = pushed_regs [2];
			INFERIOR_REG_EDX (cdata->saved_regs) = pushed_regs [3];
			INFERIOR_REG_EBP (cdata->saved_regs) = pushed_regs [4];
			INFERIOR_REG_ESP (cdata->saved_regs) = pushed_regs [5];
			INFERIOR_REG_ESI (cdata->saved_regs) = pushed_regs [6];
			INFERIOR_REG_EDI (cdata->saved_regs) = pushed_regs [7];
			INFERIOR_REG_EIP (cdata->saved_regs) = pushed_regs [8];
		}

		if (_server_ptrace_set_registers (inferior, &cdata->saved_regs) != COMMAND_ERROR_NONE)
			g_error (G_STRLOC ": Can't restore registers after returning from a call");

		if (_server_ptrace_set_fp_registers (inferior, &cdata->saved_fpregs) != COMMAND_ERROR_NONE)
			g_error (G_STRLOC ": Can't restore FP registers after returning from a call");

		*callback_arg = cdata->callback_argument;
		*retval = (guint32) INFERIOR_REG_EAX (arch->current_regs);

		if (cdata->data_pointer) {
			*opt_data_size = cdata->data_size;
			*opt_data = g_malloc0 (cdata->data_size);

			if (_server_ptrace_read_memory (
				    handle, cdata->data_pointer, cdata->data_size, *opt_data))
				g_error (G_STRLOC ": Can't read data buffer after returning from a call");
		} else {
			*opt_data_size = 0;
			*opt_data = NULL;
		}


		if (cdata->exc_address &&
		    (server_ptrace_peek_word (handle, cdata->exc_address, &exc_object) != COMMAND_ERROR_NONE))
			g_error (G_STRLOC ": Can't get exc object");

		*retval2 = (guint32) exc_object;

		inferior->last_signal = cdata->saved_signal;
		g_ptr_array_remove (arch->callback_stack, cdata);

		x86_arch_get_registers (handle);

		if (cdata->is_rti) {
			g_free (cdata);
			return STOP_ACTION_RTI_DONE;
		}

		if (cdata->debug) {
			*retval = 0;
			g_free (cdata);
			return STOP_ACTION_CALLBACK_COMPLETED;
		}

		g_free (cdata);
		return STOP_ACTION_CALLBACK;
	}

	cbuffer = arch->code_buffer;
	if (cbuffer) {
		handle->mono_runtime->executable_code_bitfield [cbuffer->slot] = 0;

		if (cbuffer->code_address + cbuffer->insn_size != INFERIOR_REG_EIP (arch->current_regs)) {
			g_warning (G_STRLOC ": %x - %x,%d - %x - %x", cbuffer->original_eip,
				   cbuffer->code_address, cbuffer->insn_size,
				   cbuffer->code_address + cbuffer->insn_size,
				   INFERIOR_REG_EIP (arch->current_regs));
			return STOP_ACTION_STOPPED;
		}

		INFERIOR_REG_EIP (arch->current_regs) = cbuffer->original_eip;
		if (cbuffer->update_ip)
			INFERIOR_REG_EIP (arch->current_regs) += cbuffer->insn_size;
		if (_server_ptrace_set_registers (inferior, &arch->current_regs) != COMMAND_ERROR_NONE) {
			g_error (G_STRLOC ": Can't restore registers");
		}

		g_free (cbuffer);
		arch->code_buffer = NULL;
		return STOP_ACTION_STOPPED;
	}

	if (server_ptrace_peek_word (handle, GPOINTER_TO_SIZE (INFERIOR_REG_EIP (arch->current_regs) - 1), &code) != COMMAND_ERROR_NONE)
		return STOP_ACTION_STOPPED;

	if ((code & 0xff) == 0xcc) {
		*retval = 0;
		return STOP_ACTION_BREAKPOINT_HIT;
	}

	return STOP_ACTION_STOPPED;
}

static ServerCommandError
server_ptrace_get_target_info (guint32 *target_int_size, guint32 *target_long_size,
			       guint32 *target_address_size, guint32 *is_bigendian)
{
	*target_int_size = sizeof (guint32);
	*target_long_size = sizeof (guint32);
	*target_address_size = sizeof (void *);
	*is_bigendian = 0;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_get_registers (ServerHandle *handle, guint64 *values)
{
	ArchInfo *arch = handle->arch;

	values [DEBUGGER_REG_RBX] = (guint32) INFERIOR_REG_EBX (arch->current_regs);
	values [DEBUGGER_REG_RCX] = (guint32) INFERIOR_REG_ECX (arch->current_regs);
	values [DEBUGGER_REG_RDX] = (guint32) INFERIOR_REG_EDX (arch->current_regs);
	values [DEBUGGER_REG_RSI] = (guint32) INFERIOR_REG_ESI (arch->current_regs);
	values [DEBUGGER_REG_RDI] = (guint32) INFERIOR_REG_EDI (arch->current_regs);
	values [DEBUGGER_REG_RBP] = (guint32) INFERIOR_REG_EBP (arch->current_regs);
	values [DEBUGGER_REG_RAX] = (guint32) INFERIOR_REG_EAX (arch->current_regs);
	values [DEBUGGER_REG_DS] = (guint32) INFERIOR_REG_DS (arch->current_regs);
	values [DEBUGGER_REG_ES] = (guint32) INFERIOR_REG_ES (arch->current_regs);
	values [DEBUGGER_REG_FS] = (guint32) INFERIOR_REG_FS (arch->current_regs);
	values [DEBUGGER_REG_GS] = (guint32) INFERIOR_REG_GS (arch->current_regs);
	values [DEBUGGER_REG_RIP] = (guint32) INFERIOR_REG_EIP (arch->current_regs);
	values [DEBUGGER_REG_CS] = (guint32) INFERIOR_REG_CS (arch->current_regs);
	values [DEBUGGER_REG_EFLAGS] = (guint32) INFERIOR_REG_EFLAGS (arch->current_regs);
	values [DEBUGGER_REG_RSP] = (guint32) INFERIOR_REG_ESP (arch->current_regs);
	values [DEBUGGER_REG_SS] = (guint32) INFERIOR_REG_SS (arch->current_regs);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_set_registers (ServerHandle *handle, guint64 *values)
{
	ArchInfo *arch = handle->arch;

	INFERIOR_REG_EBX (arch->current_regs) = values [DEBUGGER_REG_RBX];
	INFERIOR_REG_ECX (arch->current_regs) = values [DEBUGGER_REG_RCX];
	INFERIOR_REG_EDX (arch->current_regs) = values [DEBUGGER_REG_RDX];
	INFERIOR_REG_ESI (arch->current_regs) = values [DEBUGGER_REG_RSI];
	INFERIOR_REG_EDI (arch->current_regs) = values [DEBUGGER_REG_RDI];
	INFERIOR_REG_EBP (arch->current_regs) = values [DEBUGGER_REG_RBP];
	INFERIOR_REG_EAX (arch->current_regs) = values [DEBUGGER_REG_RAX];
	INFERIOR_REG_DS (arch->current_regs) = values [DEBUGGER_REG_DS];
	INFERIOR_REG_ES (arch->current_regs) = values [DEBUGGER_REG_ES];
	INFERIOR_REG_FS (arch->current_regs) = values [DEBUGGER_REG_FS];
	INFERIOR_REG_GS (arch->current_regs) = values [DEBUGGER_REG_GS];
	INFERIOR_REG_EIP (arch->current_regs) = values [DEBUGGER_REG_RIP];
	INFERIOR_REG_CS (arch->current_regs) = values [DEBUGGER_REG_CS];
	INFERIOR_REG_EFLAGS (arch->current_regs) = values [DEBUGGER_REG_EFLAGS];
	INFERIOR_REG_ESP (arch->current_regs) = values [DEBUGGER_REG_RSP];
	INFERIOR_REG_SS (arch->current_regs) = values [DEBUGGER_REG_SS];

	return _server_ptrace_set_registers (handle->inferior, &arch->current_regs);
}

static ServerCommandError
server_ptrace_push_registers (ServerHandle *handle, guint64 *new_esp)
{
	ArchInfo *arch = handle->arch;
	ServerCommandError result;

	INFERIOR_REG_ESP (arch->current_regs) -= sizeof (arch->current_regs);
	result = _server_ptrace_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	*new_esp = INFERIOR_REG_ESP (arch->current_regs);

	result = server_ptrace_write_memory (
		handle, *new_esp, sizeof (arch->current_regs), &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_pop_registers (ServerHandle *handle)
{
	ArchInfo *arch = handle->arch;
	ServerCommandError result;

	INFERIOR_REG_ESP (arch->current_regs) += sizeof (arch->current_regs);
	result = _server_ptrace_set_registers (handle->inferior, &arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}

static void
server_ptrace_get_registers_from_core_file (guint64 *values, const guint8 *buffer)
{
	INFERIOR_REGS_TYPE regs = * (INFERIOR_REGS_TYPE *) buffer;

	values [DEBUGGER_REG_RBX] = (guint32) INFERIOR_REG_EBX (regs);
	values [DEBUGGER_REG_RCX] = (guint32) INFERIOR_REG_ECX (regs);
	values [DEBUGGER_REG_RDX] = (guint32) INFERIOR_REG_EDX (regs);
	values [DEBUGGER_REG_RSI] = (guint32) INFERIOR_REG_ESI (regs);
	values [DEBUGGER_REG_RDI] = (guint32) INFERIOR_REG_EDI (regs);
	values [DEBUGGER_REG_RBP] = (guint32) INFERIOR_REG_EBP (regs);
	values [DEBUGGER_REG_RAX] = (guint32) INFERIOR_REG_EAX (regs);
	values [DEBUGGER_REG_DS] = (guint32) INFERIOR_REG_DS (regs);
	values [DEBUGGER_REG_ES] = (guint32) INFERIOR_REG_ES (regs);
	values [DEBUGGER_REG_FS] = (guint32) INFERIOR_REG_FS (regs);
	values [DEBUGGER_REG_GS] = (guint32) INFERIOR_REG_GS (regs);
	values [DEBUGGER_REG_RIP] = (guint32) INFERIOR_REG_EIP (regs);
	values [DEBUGGER_REG_CS] = (guint32) INFERIOR_REG_CS (regs);
	values [DEBUGGER_REG_EFLAGS] = (guint32) INFERIOR_REG_EFLAGS (regs);
	values [DEBUGGER_REG_RSP] = (guint32) INFERIOR_REG_ESP (regs);
	values [DEBUGGER_REG_SS] = (guint32) INFERIOR_REG_SS (regs);
}

static int
find_breakpoint_table_slot (MonoRuntimeInfo *runtime)
{
	int i;

	for (i = 1; i < runtime->breakpoint_table_size; i++) {
		if (runtime->breakpoint_table_bitfield [i])
			continue;

		runtime->breakpoint_table_bitfield [i] = 1;
		return i;
	}

	return -1;
}

static ServerCommandError
runtime_info_enable_breakpoint (ServerHandle *handle, BreakpointInfo *breakpoint)
{
	MonoRuntimeInfo *runtime;
	ServerCommandError result;
	guint64 table_address, index_address;
	int slot;

	runtime = handle->mono_runtime;
	g_assert (runtime);

	slot = find_breakpoint_table_slot (runtime);
	if (slot < 0)
		return COMMAND_ERROR_INTERNAL_ERROR;

	breakpoint->runtime_table_slot = slot;

	table_address = runtime->breakpoint_info_area + 8 * slot;
	index_address = runtime->breakpoint_table + 4 * slot;

	result = server_ptrace_poke_word (handle, table_address, breakpoint->address);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = server_ptrace_poke_word (handle, table_address + 4, (gsize) breakpoint->saved_insn);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = server_ptrace_poke_word (handle, index_address, (gsize) slot);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
runtime_info_disable_breakpoint (ServerHandle *handle, BreakpointInfo *breakpoint)
{
	MonoRuntimeInfo *runtime;
	ServerCommandError result;
	guint64 index_address;
	int slot;

	runtime = handle->mono_runtime;
	g_assert (runtime);

	slot = breakpoint->runtime_table_slot;
	index_address = runtime->breakpoint_table + runtime->address_size * slot;

	result = server_ptrace_poke_word (handle, index_address, 0);
	if (result != COMMAND_ERROR_NONE)
		return result;

	runtime->breakpoint_table_bitfield [slot] = 0;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
x86_arch_enable_breakpoint (ServerHandle *handle, BreakpointInfo *breakpoint)
{
	ServerCommandError result;
	ArchInfo *arch = handle->arch;
	InferiorHandle *inferior = handle->inferior;
	char bopcode = 0xcc;
	guint32 address;

	if (breakpoint->enabled)
		return COMMAND_ERROR_NONE;

	address = (guint32) breakpoint->address;

	if (breakpoint->dr_index >= 0) {
		X86_DR_SET_RW_LEN (arch, breakpoint->dr_index, DR_RW_EXECUTE | DR_LEN_1);
		X86_DR_LOCAL_ENABLE (arch, breakpoint->dr_index);

		result = _server_ptrace_set_dr (inferior, breakpoint->dr_index, address);
		if (result != COMMAND_ERROR_NONE) {
			g_warning (G_STRLOC);
			return result;
		}

		result = _server_ptrace_set_dr (inferior, DR_CONTROL, arch->dr_control);
		if (result != COMMAND_ERROR_NONE) {
			g_warning (G_STRLOC);
			return result;
		}

		arch->dr_regs [breakpoint->dr_index] = breakpoint->id;
	} else {
		result = server_ptrace_read_memory (handle, address, 1, &breakpoint->saved_insn);
		if (result != COMMAND_ERROR_NONE)
			return result;

		if (handle->mono_runtime) {
			result = runtime_info_enable_breakpoint (handle, breakpoint);
			if (result != COMMAND_ERROR_NONE)
				return result;
		}

		result = server_ptrace_write_memory (handle, address, 1, &bopcode);
		if (result != COMMAND_ERROR_NONE)
			return result;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
x86_arch_disable_breakpoint (ServerHandle *handle, BreakpointInfo *breakpoint)
{
	ServerCommandError result;
	ArchInfo *arch = handle->arch;
	InferiorHandle *inferior = handle->inferior;
	guint32 address;

	if (!breakpoint->enabled)
		return COMMAND_ERROR_NONE;

	address = (guint32) breakpoint->address;

	if (breakpoint->dr_index >= 0) {
		X86_DR_DISABLE (arch, breakpoint->dr_index);

		result = _server_ptrace_set_dr (inferior, breakpoint->dr_index, 0L);
		if (result != COMMAND_ERROR_NONE) {
			g_warning (G_STRLOC ": %d", result);
			return result;
		}

		result = _server_ptrace_set_dr (inferior, DR_CONTROL, arch->dr_control);
		if (result != COMMAND_ERROR_NONE) {
			g_warning (G_STRLOC ": %d", result);
			return result;
		}

		arch->dr_regs [breakpoint->dr_index] = 0;
	} else {
		result = server_ptrace_write_memory (handle, address, 1, &breakpoint->saved_insn);
		if (result != COMMAND_ERROR_NONE)
			return result;

		if (handle->mono_runtime) {
			result = runtime_info_disable_breakpoint (handle, breakpoint);
			if (result != COMMAND_ERROR_NONE)
				return result;
		}
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_insert_breakpoint (ServerHandle *handle, guint64 address, guint32 *bhandle)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup (handle->bpm, address);
	if (breakpoint) {
		/*
		 * You cannot have a hardware breakpoint and a normal breakpoint on the same
		 * instruction.
		 */
		if (breakpoint->is_hardware_bpt) {
			mono_debugger_breakpoint_manager_unlock ();
			return COMMAND_ERROR_DR_OCCUPIED;
		}

		breakpoint->refcount++;
		goto done;
	}

	breakpoint = g_new0 (BreakpointInfo, 1);

	breakpoint->refcount = 1;
	breakpoint->address = address;
	breakpoint->is_hardware_bpt = FALSE;
	breakpoint->id = mono_debugger_breakpoint_manager_get_next_id ();
	breakpoint->dr_index = -1;

	result = x86_arch_enable_breakpoint (handle, breakpoint);
	if (result != COMMAND_ERROR_NONE) {
		mono_debugger_breakpoint_manager_unlock ();
		g_free (breakpoint);
		return result;
	}

	breakpoint->enabled = TRUE;
	mono_debugger_breakpoint_manager_insert (handle->bpm, (BreakpointInfo *) breakpoint);
 done:
	*bhandle = breakpoint->id;
	mono_debugger_breakpoint_manager_unlock ();

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_remove_breakpoint (ServerHandle *handle, guint32 bhandle)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (handle->bpm, bhandle);
	if (!breakpoint) {
		result = COMMAND_ERROR_NO_SUCH_BREAKPOINT;
		goto out;
	}

	if (--breakpoint->refcount > 0) {
		result = COMMAND_ERROR_NONE;
		goto out;
	}

	result = x86_arch_disable_breakpoint (handle, breakpoint);
	if (result != COMMAND_ERROR_NONE)
		goto out;

	breakpoint->enabled = FALSE;
	mono_debugger_breakpoint_manager_remove (handle->bpm, (BreakpointInfo *) breakpoint);

 out:
	mono_debugger_breakpoint_manager_unlock ();
	return result;
}

static void
x86_arch_remove_hardware_breakpoints (ServerHandle *handle)
{
	int i;

	for (i = 0; i < DR_NADDR; i++) {
		X86_DR_DISABLE (handle->arch, i);

		_server_ptrace_set_dr (handle->inferior, i, 0L);
		_server_ptrace_set_dr (handle->inferior, DR_CONTROL, handle->arch->dr_control);

		handle->arch->dr_regs [i] = 0;
	}
}

static ServerCommandError
find_free_hw_register (ServerHandle *handle, guint32 *idx)
{
	int i;

	for (i = 0; i < DR_NADDR; i++) {
		if (!handle->arch->dr_regs [i]) {
			*idx = i;
			return COMMAND_ERROR_NONE;
		}
	}

	return COMMAND_ERROR_DR_OCCUPIED;
}

static ServerCommandError
server_ptrace_insert_hw_breakpoint (ServerHandle *handle, guint32 type, guint32 *idx,
				    guint64 address, guint32 *bhandle)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup (handle->bpm, address);
	if (breakpoint) {
		breakpoint->refcount++;
		goto done;
	}

	result = find_free_hw_register (handle, idx);
	if (result != COMMAND_ERROR_NONE) {
		mono_debugger_breakpoint_manager_unlock ();
		return result;
	}

	breakpoint = g_new0 (BreakpointInfo, 1);
	breakpoint->type = (HardwareBreakpointType) type;
	breakpoint->address = address;
	breakpoint->refcount = 1;
	breakpoint->id = mono_debugger_breakpoint_manager_get_next_id ();
	breakpoint->is_hardware_bpt = TRUE;
	breakpoint->dr_index = *idx;

	result = x86_arch_enable_breakpoint (handle, breakpoint);
	if (result != COMMAND_ERROR_NONE) {
		mono_debugger_breakpoint_manager_unlock ();
		g_free (breakpoint);
		return result;
	}

	breakpoint->enabled = TRUE;
	mono_debugger_breakpoint_manager_insert (handle->bpm, (BreakpointInfo *) breakpoint);
 done:
	*bhandle = breakpoint->id;
	mono_debugger_breakpoint_manager_unlock ();

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_enable_breakpoint (ServerHandle *handle, guint32 bhandle)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (handle->bpm, bhandle);
	if (!breakpoint) {
		mono_debugger_breakpoint_manager_unlock ();
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;
	}

	result = x86_arch_enable_breakpoint (handle, breakpoint);
	breakpoint->enabled = TRUE;
	mono_debugger_breakpoint_manager_unlock ();
	return result;
}

static ServerCommandError
server_ptrace_disable_breakpoint (ServerHandle *handle, guint32 bhandle)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (handle->bpm, bhandle);
	if (!breakpoint) {
		mono_debugger_breakpoint_manager_unlock ();
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;
	}

	result = x86_arch_disable_breakpoint (handle, breakpoint);
	breakpoint->enabled = FALSE;
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

static int
find_code_buffer_slot (MonoRuntimeInfo *runtime)
{
	int i;

	for (i = 0; i < runtime->executable_code_total_chunks; i++) {
		if (runtime->executable_code_bitfield [i])
			continue;

		runtime->executable_code_bitfield [i] = 1;
		return i;
	}

	return -1;
}

static ServerCommandError
server_ptrace_execute_instruction (ServerHandle *handle, const guint8 *instruction,
				   guint32 size, gboolean update_ip)
{
	MonoRuntimeInfo *runtime;
	ServerCommandError result;
	CodeBufferData *data;
	guint32 code_address;
	int slot;

	runtime = handle->mono_runtime;
	g_assert (runtime);

	if (!runtime->executable_code_buffer)
		return COMMAND_ERROR_INTERNAL_ERROR;

	slot = find_code_buffer_slot (runtime);
	if (slot < 0)
		return COMMAND_ERROR_INTERNAL_ERROR;

	if (size > runtime->executable_code_chunk_size)
		return COMMAND_ERROR_INTERNAL_ERROR;
	if (handle->arch->code_buffer)
		return COMMAND_ERROR_INTERNAL_ERROR;

	code_address = runtime->executable_code_buffer + slot * runtime->executable_code_chunk_size;

	data = g_new0 (CodeBufferData, 1);
	data->slot = slot;
	data->insn_size = size;
	data->update_ip = update_ip;
	data->original_eip = INFERIOR_REG_EIP (handle->arch->current_regs);
	data->code_address = code_address;

	handle->arch->code_buffer = data;

	result = server_ptrace_write_memory (handle, code_address, size, instruction);
	if (result != COMMAND_ERROR_NONE)
		return result;

	INFERIOR_REG_ORIG_EAX (handle->arch->current_regs) = -1;
	INFERIOR_REG_EIP (handle->arch->current_regs) = code_address;

	result = _server_ptrace_set_registers (handle->inferior, &handle->arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return server_ptrace_step (handle);
}

static ServerCommandError
server_ptrace_mark_rti_frame (ServerHandle *handle)
{
	CallbackData *cdata;

	cdata = get_callback_data (handle->arch);
	if (!cdata)
		return COMMAND_ERROR_NO_CALLBACK_FRAME;

	cdata->rti_frame = INFERIOR_REG_ESP (handle->arch->current_regs) + 4;
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_abort_invoke (ServerHandle *handle, guint64 rti_id)
{
	CallbackData *cdata;

	cdata = get_callback_data (handle->arch);
	if (!cdata || !cdata->is_rti || (cdata->callback_argument != rti_id))
		return COMMAND_ERROR_NO_CALLBACK_FRAME;

	if (_server_ptrace_set_registers (handle->inferior, &cdata->saved_regs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't restore registers after returning from a call");

	if (_server_ptrace_set_fp_registers (handle->inferior, &cdata->saved_fpregs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't restore FP registers after returning from a call");

	handle->inferior->last_signal = cdata->saved_signal;
	g_ptr_array_remove (handle->arch->callback_stack, cdata);

	x86_arch_get_registers (handle);
	g_free (cdata);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_get_callback_frame (ServerHandle *handle, guint64 stack_pointer,
				  gboolean exact_match, guint64 *registers)
{
	int i;

	for (i = 0; i < handle->arch->callback_stack->len; i++) {
		CallbackData *cdata = g_ptr_array_index (handle->arch->callback_stack, i);

		if (cdata->rti_frame) {
			if (exact_match) {
				if (cdata->rti_frame != stack_pointer)
					continue;
			} else {
				if (cdata->rti_frame < stack_pointer)
					continue;
			}
		} else {
			if (exact_match) {
				if (cdata->stack_pointer != stack_pointer)
					continue;
			} else {
				if (cdata->stack_pointer < stack_pointer)
					continue;
			}
		}

		registers [DEBUGGER_REG_RBX] = (guint32) INFERIOR_REG_EBX (cdata->saved_regs);
		registers [DEBUGGER_REG_RCX] = (guint32) INFERIOR_REG_ECX (cdata->saved_regs);
		registers [DEBUGGER_REG_RDX] = (guint32) INFERIOR_REG_EDX (cdata->saved_regs);
		registers [DEBUGGER_REG_RSI] = (guint32) INFERIOR_REG_ESI (cdata->saved_regs);
		registers [DEBUGGER_REG_RDI] = (guint32) INFERIOR_REG_EDI (cdata->saved_regs);
		registers [DEBUGGER_REG_RBP] = (guint32) INFERIOR_REG_EBP (cdata->saved_regs);
		registers [DEBUGGER_REG_RAX] = (guint32) INFERIOR_REG_EAX (cdata->saved_regs);
		registers [DEBUGGER_REG_DS] = (guint32) INFERIOR_REG_DS (cdata->saved_regs);
		registers [DEBUGGER_REG_ES] = (guint32) INFERIOR_REG_ES (cdata->saved_regs);
		registers [DEBUGGER_REG_FS] = (guint32) INFERIOR_REG_FS (cdata->saved_regs);
		registers [DEBUGGER_REG_GS] = (guint32) INFERIOR_REG_GS (cdata->saved_regs);
		registers [DEBUGGER_REG_RIP] = (guint32) INFERIOR_REG_EIP (cdata->saved_regs);
		registers [DEBUGGER_REG_CS] = (guint32) INFERIOR_REG_CS (cdata->saved_regs);
		registers [DEBUGGER_REG_EFLAGS] = (guint32) INFERIOR_REG_EFLAGS (cdata->saved_regs);
		registers [DEBUGGER_REG_RSP] = (guint32) INFERIOR_REG_ESP (cdata->saved_regs);
		registers [DEBUGGER_REG_SS] = (guint32) INFERIOR_REG_SS (cdata->saved_regs);

		return COMMAND_ERROR_NONE;
	}

	return COMMAND_ERROR_NO_CALLBACK_FRAME;
}

static ServerCommandError
server_ptrace_restart_notification (ServerHandle *handle)
{
	ServerCommandError result;

	if (!handle->mono_runtime ||
	    (INFERIOR_REG_EIP (handle->arch->current_regs) - 1 != handle->mono_runtime->notification_address))
		return COMMAND_ERROR_INTERNAL_ERROR;

	INFERIOR_REG_EIP (handle->arch->current_regs)--;
	return _server_ptrace_set_registers (handle->inferior, &handle->arch->current_regs);
}
