#define _GNU_SOURCE
#include <server.h>
#include <breakpoints.h>
#include <sys/stat.h>
#include <sys/ptrace.h>
#include <asm/ptrace.h>
#include <asm/user.h>
#include <sys/wait.h>
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

struct InferiorHandle
{
	int pid;
	int mem_fd;
	int last_signal;
	long call_address;
	guint64 callback_argument;
	struct user_regs_struct current_regs;
	struct user_i387_struct current_fpregs;
	struct user_regs_struct *saved_regs;
	struct user_i387_struct *saved_fpregs;
	unsigned dr_control, dr_status;
	BreakpointManager *bpm;
};

/* Debug registers' indices.  */
#define DR_NADDR		4  /* the number of debug address registers */
#define DR_STATUS		6  /* index of debug status register (DR6) */
#define DR_CONTROL		7  /* index of debug control register (DR7) */

/* DR7 Debug Control register fields.  */

/* How many bits to skip in DR7 to get to R/W and LEN fields.  */
#define DR_CONTROL_SHIFT	16
/* How many bits in DR7 per R/W and LEN field for each watchpoint.  */
#define DR_CONTROL_SIZE		4

/* Watchpoint/breakpoint read/write fields in DR7.  */
#define DR_RW_EXECUTE		(0x0) /* break on instruction execution */
#define DR_RW_WRITE		(0x1) /* break on data writes */
#define DR_RW_READ		(0x3) /* break on data reads or writes */

/* This is here for completeness.  No platform supports this
   functionality yet (as of Mar-2001).  Note that the DE flag in the
   CR4 register needs to be set to support this.  */
#ifndef DR_RW_IORW
#define DR_RW_IORW		(0x2) /* break on I/O reads or writes */
#endif

/* Watchpoint/breakpoint length fields in DR7.  The 2-bit left shift
   is so we could OR this with the read/write field defined above.  */
#define DR_LEN_1		(0x0 << 2) /* 1-byte region watch or breakpt */
#define DR_LEN_2		(0x1 << 2) /* 2-byte region watch */
#define DR_LEN_4		(0x3 << 2) /* 4-byte region watch */
#define DR_LEN_8		(0x2 << 2) /* 8-byte region watch (x86-64) */

/* Local and Global Enable flags in DR7. */
#define DR_LOCAL_ENABLE_SHIFT	0   /* extra shift to the local enable bit */
#define DR_GLOBAL_ENABLE_SHIFT	1   /* extra shift to the global enable bit */
#define DR_ENABLE_SIZE		2   /* 2 enable bits per debug register */

/* The I'th debug register is vacant if its Local and Global Enable
   bits are reset in the Debug Control register.  */
#define I386_DR_VACANT(handle,i) \
  ((handle->dr_control & (3 << (DR_ENABLE_SIZE * (i)))) == 0)

/* Locally enable the break/watchpoint in the I'th debug register.  */
#define I386_DR_LOCAL_ENABLE(handle,i) \
  handle->dr_control |= (1 << (DR_LOCAL_ENABLE_SHIFT + DR_ENABLE_SIZE * (i)))

/* Globally enable the break/watchpoint in the I'th debug register.  */
#define I386_DR_GLOBAL_ENABLE(handle,i) \
  handle->dr_control |= (1 << (DR_GLOBAL_ENABLE_SHIFT + DR_ENABLE_SIZE * (i)))

/* Disable the break/watchpoint in the I'th debug register.  */
#define I386_DR_DISABLE(handle,i) \
  handle->dr_control &= ~(3 << (DR_ENABLE_SIZE * (i)))

/* Set in DR7 the RW and LEN fields for the I'th debug register.  */
#define I386_DR_SET_RW_LEN(handle,i,rwlen) \
  do { \
    handle->dr_control &= ~(0x0f << (DR_CONTROL_SHIFT+DR_CONTROL_SIZE*(i)));   \
    handle->dr_control |= ((rwlen) << (DR_CONTROL_SHIFT+DR_CONTROL_SIZE*(i))); \
  } while (0)

/* Get from DR7 the RW and LEN fields for the I'th debug register.  */
#define I386_DR_GET_RW_LEN(handle,i) \
  ((handle->dr_control >> (DR_CONTROL_SHIFT + DR_CONTROL_SIZE * (i))) & 0x0f)

/* Did the watchpoint whose address is in the I'th register break?  */
#define I386_DR_WATCH_HIT(handle,i) \
  (handle->dr_status & (1 << (i)))

typedef struct {
	BreakpointInfo info;
	int dr_index;
	char saved_insn;
} I386BreakpointInfo;

typedef enum {
	STOP_ACTION_SEND_STOPPED,
	STOP_ACTION_BREAKPOINT_HIT,
	STOP_ACTION_CALLBACK
} ChildStoppedAction;

static ServerCommandError
get_registers (InferiorHandle *handle, struct user_regs_struct *regs)
{
	if (ptrace (PTRACE_GETREGS, handle->pid, NULL, regs) != 0) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;
		else if (errno) {
			g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
			return COMMAND_ERROR_UNKNOWN;
		}
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
set_registers (InferiorHandle *handle, struct user_regs_struct *regs)
{
	if (ptrace (PTRACE_SETREGS, handle->pid, NULL, regs) != 0) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;
		else if (errno) {
			g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
			return COMMAND_ERROR_UNKNOWN;
		}
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
get_fp_registers (InferiorHandle *handle, struct user_i387_struct *regs)
{
	if (ptrace (PTRACE_GETFPREGS, handle->pid, NULL, regs) != 0) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;
		else if (errno) {
			g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
			return COMMAND_ERROR_UNKNOWN;
		}
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
set_fp_registers (InferiorHandle *handle, struct user_i387_struct *regs)
{
	if (ptrace (PTRACE_SETFPREGS, handle->pid, NULL, regs) != 0) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;
		else if (errno) {
			g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
			return COMMAND_ERROR_UNKNOWN;
		}
	}

	return COMMAND_ERROR_NONE;
}

static void
server_ptrace_finalize (InferiorHandle *handle)
{
	if (handle->pid)
		ptrace (PTRACE_KILL, handle->pid, NULL, NULL);

	g_free (handle->saved_regs);
	g_free (handle->saved_fpregs);
	g_free (handle);
}

static ServerCommandError
server_ptrace_continue (InferiorHandle *handle)
{
	errno = 0;
	if (ptrace (PTRACE_CONT, handle->pid, NULL, GUINT_TO_POINTER (handle->last_signal))) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;

		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_step (InferiorHandle *handle)
{
	errno = 0;
	if (ptrace (PTRACE_SINGLESTEP, handle->pid, NULL, GUINT_TO_POINTER (handle->last_signal))) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;

		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_detach (InferiorHandle *handle)
{
	if (ptrace (PTRACE_DETACH, handle->pid, NULL, NULL)) {
		g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_get_pc (InferiorHandle *handle, guint64 *pc)
{
	*pc = (guint32) handle->current_regs.eip;
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_current_insn_is_bpt (InferiorHandle *handle, guint32 *is_breakpoint)
{
	mono_debugger_breakpoint_manager_lock (handle->bpm);
	if (mono_debugger_breakpoint_manager_lookup (handle->bpm, handle->current_regs.eip))
		*is_breakpoint = TRUE;
	else
		*is_breakpoint = FALSE;
	mono_debugger_breakpoint_manager_unlock (handle->bpm);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_read_data (InferiorHandle *handle, guint64 start, guint32 size, gpointer buffer)
{
	guint8 *ptr = buffer;

	while (size) {
		int ret = pread64 (handle->mem_fd, ptr, size, start);
		if (ret < 0) {
			if (errno == EINTR)
				continue;
			g_warning (G_STRLOC ": Can't read target memory at address %08Lx: %s",
				   start, g_strerror (errno));
			return COMMAND_ERROR_UNKNOWN;
		}

		size -= ret;
		ptr += ret;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_peek_word (InferiorHandle *handle, guint64 start, int *retval)
{
	return server_ptrace_read_data (handle, start, sizeof (int), retval);

	errno = 0;
	*retval = ptrace (PTRACE_PEEKDATA, handle->pid, start, NULL);
	if (errno) {
		g_message (G_STRLOC ": %d - %08Lx - %s", handle->pid, start, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_set_dr (InferiorHandle *handle, int regnum, unsigned long value)
{
	errno = 0;
	ptrace (PTRACE_POKEUSER, handle->pid, offsetof (struct user, u_debugreg [regnum]), value);
	if (errno) {
		g_message (G_STRLOC ": %d - %d - %s", handle->pid, regnum, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_write_data (InferiorHandle *handle, guint64 start, guint32 size, gconstpointer buffer)
{
	ServerCommandError result;
	const int *ptr = buffer;
	int addr = start;
	char temp [4];

	while (size >= 4) {
		int word = *ptr++;

		errno = 0;
		if (ptrace (PTRACE_POKEDATA, handle->pid, addr, word) != 0) {
			if (errno == ESRCH)
				return COMMAND_ERROR_NOT_STOPPED;
			else if (errno) {
				g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
				return COMMAND_ERROR_UNKNOWN;
			}
		}

		addr += sizeof (int);
		size -= sizeof (int);
	}

	if (!size)
		return COMMAND_ERROR_NONE;

	result = server_ptrace_read_data (handle, (guint32) addr, 4, &temp);
	if (result != COMMAND_ERROR_NONE)
		return result;
	memcpy (&temp, ptr, size);

	return server_ptrace_write_data (handle, (guint32) addr, 4, &temp);
}	

/*
 * This method is highly architecture and specific.
 * It will only work on the i386.
 */

static ServerCommandError
server_ptrace_call_method (InferiorHandle *handle, guint64 method_address,
			   guint64 method_argument, guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	long new_esp, call_disp;

	guint8 code[] = { 0x68, 0x00, 0x00, 0x00, 0x00, 0x68, 0x00, 0x00,
			  0x00, 0x00, 0xe8, 0x00, 0x00, 0x00, 0x00, 0xcc };
	int size = sizeof (code);

	if (handle->saved_regs)
		return COMMAND_ERROR_RECURSIVE_CALL;

	new_esp = handle->current_regs.esp - size;

	handle->saved_regs = g_memdup (&handle->current_regs, sizeof (handle->current_regs));
	handle->saved_fpregs = g_memdup (&handle->current_fpregs, sizeof (handle->current_fpregs));
	handle->call_address = new_esp + 16;
	handle->callback_argument = callback_argument;

	call_disp = (int) method_address - new_esp;

	*((guint32 *) (code+1)) = method_argument >> 32;
	*((guint32 *) (code+6)) = method_argument & 0xffffffff;
	*((guint32 *) (code+11)) = call_disp - 15;

	result = server_ptrace_write_data (handle, (unsigned long) new_esp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	handle->current_regs.esp = handle->current_regs.eip = new_esp;

	result = set_registers (handle, &handle->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return server_ptrace_continue (handle);
}

/*
 * This method is highly architecture and specific.
 * It will only work on the i386.
 */

static ServerCommandError
server_ptrace_call_method_1 (InferiorHandle *handle, guint64 method_address,
			     guint64 method_argument, const gchar *string_argument,
			     guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	long new_esp, call_disp;

	static guint8 static_code[] = { 0x68, 0x00, 0x00, 0x00, 0x00, 0x68, 0x00, 0x00,
					0x00, 0x00, 0x68, 0x00, 0x00, 0x00, 0x00, 0xe8,
					0x00, 0x00, 0x00, 0x00, 0xcc };
	int static_size = sizeof (static_code);
	int size = static_size + strlen (string_argument) + 1;
	guint8 *code = g_malloc0 (size);
	memcpy (code, static_code, static_size);
	strcpy (code + static_size, string_argument);

	if (handle->saved_regs)
		return COMMAND_ERROR_RECURSIVE_CALL;

	new_esp = handle->current_regs.esp - size;

	handle->saved_regs = g_memdup (&handle->current_regs, sizeof (handle->current_regs));
	handle->saved_fpregs = g_memdup (&handle->current_fpregs, sizeof (handle->current_fpregs));
	handle->call_address = new_esp + 21;
	handle->callback_argument = callback_argument;

	call_disp = (int) method_address - new_esp;

	*((guint32 *) (code+1)) = new_esp + 21;
	*((guint32 *) (code+6)) = method_argument >> 32;
	*((guint32 *) (code+11)) = method_argument & 0xffffffff;
	*((guint32 *) (code+16)) = call_disp - 20;

	result = server_ptrace_write_data (handle, (unsigned long) new_esp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	handle->current_regs.esp = handle->current_regs.eip = new_esp;

	result = set_registers (handle, &handle->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return server_ptrace_continue (handle);
}

static ServerCommandError
server_ptrace_call_method_invoke (InferiorHandle *handle, guint64 invoke_method,
				  guint64 method_argument, guint64 object_argument,
				  guint32 num_params, guint64 *param_data,
				  guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	long new_esp, call_disp;
	int i;

	static guint8 static_code[] = { 0x68, 0x00, 0x00, 0x00, 0x00, 0x68, 0x00, 0x00,
					0x00, 0x00, 0x68, 0x00, 0x00, 0x00, 0x00, 0x68,
					0x00, 0x00, 0x00, 0x00, 0xe8, 0x00, 0x00, 0x00,
					0x00, 0xe8, 0x00, 0x00, 0x00, 0x00, 0x5a, 0x8d,
					0x92, 0x00, 0x00, 0x00, 0x00, 0x8b, 0x12, 0x31,
					0xdb, 0x31, 0xc9, 0xcc };
	int static_size = sizeof (static_code);
	int size = static_size + (num_params + 2) * 4;
	guint8 *code = g_malloc0 (size);
	guint32 *ptr = (guint32 *) (code + static_size);
	memcpy (code, static_code, static_size);

	for (i = 0; i < num_params; i++)
		ptr [i] = param_data [i];
	ptr [num_params] = 0;

	if (handle->saved_regs)
		return COMMAND_ERROR_RECURSIVE_CALL;

	new_esp = handle->current_regs.esp - size;

	handle->saved_regs = g_memdup (&handle->current_regs, sizeof (handle->current_regs));
	handle->saved_fpregs = g_memdup (&handle->current_fpregs, sizeof (handle->current_fpregs));
	handle->call_address = new_esp + static_size;
	handle->callback_argument = callback_argument;

	call_disp = (int) invoke_method - new_esp;

	*((guint32 *) (code+1)) = new_esp + static_size + (num_params + 1) * 4;
	*((guint32 *) (code+6)) = new_esp + static_size;
	*((guint32 *) (code+11)) = object_argument;
	*((guint32 *) (code+16)) = method_argument;
	*((guint32 *) (code+21)) = call_disp - 25;
	*((guint32 *) (code+33)) = 14 + (num_params + 1) * 4;

	result = server_ptrace_write_data (handle, (unsigned long) new_esp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	handle->current_regs.esp = handle->current_regs.eip = new_esp;

	result = set_registers (handle, &handle->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return server_ptrace_continue (handle);
}

static gboolean
check_breakpoint (InferiorHandle *handle, guint64 address, guint64 *retval)
{
	I386BreakpointInfo *info;

	mono_debugger_breakpoint_manager_lock (handle->bpm);
	info = (I386BreakpointInfo *) mono_debugger_breakpoint_manager_lookup (handle->bpm, address);
	if (!info || !info->info.enabled) {
		mono_debugger_breakpoint_manager_unlock (handle->bpm);
		return FALSE;
	}

	*retval = info->info.id;
	mono_debugger_breakpoint_manager_unlock (handle->bpm);
	return TRUE;
}

static ChildStoppedAction
server_ptrace_child_stopped (InferiorHandle *handle, int stopsig,
			     guint64 *callback_arg, guint64 *retval, guint64 *retval2)
{
	if (get_registers (handle, &handle->current_regs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't get registers");
	if (get_fp_registers (handle, &handle->current_fpregs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't get fp registers");

	if (check_breakpoint (handle, handle->current_regs.eip - 1, retval)) {
		handle->current_regs.eip--;
		set_registers (handle, &handle->current_regs);
		return STOP_ACTION_BREAKPOINT_HIT;
	}

	if (!handle->call_address || handle->call_address != handle->current_regs.eip) {
		int code;

		if (stopsig != SIGTRAP) {
			handle->last_signal = stopsig;
			return STOP_ACTION_SEND_STOPPED;
		}

		if (server_ptrace_peek_word (handle, (guint32) (handle->current_regs.eip - 1), &code) != COMMAND_ERROR_NONE)
			return STOP_ACTION_SEND_STOPPED;

		if ((code & 0xff) == 0xcc) {
			*retval = 0;
			handle->current_regs.eip--;
			set_registers (handle, &handle->current_regs);
			return STOP_ACTION_BREAKPOINT_HIT;
		}

		return STOP_ACTION_SEND_STOPPED;
	}

	if (set_registers (handle, handle->saved_regs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't restore registers after returning from a call");

	if (set_fp_registers (handle, handle->saved_fpregs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't restore FP registers after returning from a call");

	*callback_arg = handle->callback_argument;
	*retval = (((guint64) handle->current_regs.ecx) << 32) + ((gulong) handle->current_regs.eax);
	*retval2 = (((guint64) handle->current_regs.ebx) << 32) + ((gulong) handle->current_regs.edx);

	g_free (handle->saved_regs);
	g_free (handle->saved_fpregs);

	handle->saved_regs = NULL;
	handle->saved_fpregs = NULL;
	handle->call_address = 0;
	handle->callback_argument = 0;

	if (get_registers (handle, &handle->current_regs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't get registers");
	if (get_fp_registers (handle, &handle->current_fpregs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't get fp registers");

	return STOP_ACTION_CALLBACK;
}

static ServerCommandError
do_enable (InferiorHandle *handle, I386BreakpointInfo *breakpoint)
{
	ServerCommandError result;
	char bopcode = 0xcc;
	guint32 address;

	if (breakpoint->info.enabled)
		return COMMAND_ERROR_NONE;

	address = (guint32) breakpoint->info.address;

	if (breakpoint->dr_index >= 0) {
		I386_DR_SET_RW_LEN (handle, breakpoint->dr_index, DR_RW_EXECUTE | DR_LEN_1);
		I386_DR_LOCAL_ENABLE (handle, breakpoint->dr_index);

		result = server_ptrace_set_dr (handle, breakpoint->dr_index, address);
		if (result != COMMAND_ERROR_NONE)
			return result;

		result = server_ptrace_set_dr (handle, DR_CONTROL, handle->dr_control);
		if (result != COMMAND_ERROR_NONE)
			return result;
	} else {
		result = server_ptrace_read_data (handle, address, 1, &breakpoint->saved_insn);
		if (result != COMMAND_ERROR_NONE)
			return result;

		result = server_ptrace_write_data (handle, address, 1, &bopcode);
		if (result != COMMAND_ERROR_NONE)
			return result;
	}

	breakpoint->info.enabled = TRUE;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
do_disable (InferiorHandle *handle, I386BreakpointInfo *breakpoint)
{
	ServerCommandError result;
	guint32 address;

	if (!breakpoint->info.enabled)
		return COMMAND_ERROR_NONE;

	address = (guint32) breakpoint->info.address;

	if (breakpoint->dr_index >= 0) {
		I386_DR_DISABLE (handle, breakpoint->dr_index);

		result = server_ptrace_set_dr (handle, breakpoint->dr_index, 0L);
		if (result != COMMAND_ERROR_NONE)
			return result;

		result = server_ptrace_set_dr (handle, DR_CONTROL, handle->dr_control);
		if (result != COMMAND_ERROR_NONE)
			return result;
	} else {
		result = server_ptrace_write_data (handle, address, 1, &breakpoint->saved_insn);
		if (result != COMMAND_ERROR_NONE)
			return result;
	}

	breakpoint->info.enabled = FALSE;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_insert_breakpoint (InferiorHandle *handle, guint64 address, guint32 *bhandle)
{
	I386BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock (handle->bpm);
	breakpoint = (I386BreakpointInfo *) mono_debugger_breakpoint_manager_lookup (handle->bpm, address);
	if (breakpoint && !breakpoint->info.is_hardware_bpt) {
		breakpoint->info.refcount++;
		goto done;
	}

	breakpoint = g_new0 (I386BreakpointInfo, 1);

	breakpoint->info.refcount = 1;
	breakpoint->info.owner = handle->pid;
	breakpoint->info.address = address;
	breakpoint->info.is_hardware_bpt = FALSE;
	breakpoint->info.id = mono_debugger_breakpoint_manager_get_next_id ();
	breakpoint->dr_index = -1;

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
server_ptrace_remove_breakpoint (InferiorHandle *handle, guint32 bhandle)
{
	I386BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock (handle->bpm);
	breakpoint = (I386BreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (handle->bpm, bhandle);
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
server_ptrace_insert_hw_breakpoint (InferiorHandle *handle, guint32 idx, guint64 address, guint32 *bhandle)
{
	I386BreakpointInfo *breakpoint;
	ServerCommandError result;

	if ((idx < 0) || (idx > DR_NADDR))
		return COMMAND_ERROR_DR_OCCUPIED;

	if (!I386_DR_VACANT (handle, idx))
		return COMMAND_ERROR_DR_OCCUPIED;

	mono_debugger_breakpoint_manager_lock (handle->bpm);
	breakpoint = g_new0 (I386BreakpointInfo, 1);
	breakpoint->info.owner = handle->pid;
	breakpoint->info.address = address;
	breakpoint->info.refcount = 1;
	breakpoint->info.id = mono_debugger_breakpoint_manager_get_next_id ();
	breakpoint->info.is_hardware_bpt = TRUE;
	breakpoint->dr_index = idx;

	result = do_enable (handle, breakpoint);
	if (result != COMMAND_ERROR_NONE) {
		mono_debugger_breakpoint_manager_unlock (handle->bpm);
		g_free (breakpoint);
		return result;
	}

	mono_debugger_breakpoint_manager_insert (handle->bpm, (BreakpointInfo *) breakpoint);
	*bhandle = breakpoint->info.id;
	mono_debugger_breakpoint_manager_unlock (handle->bpm);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_enable_breakpoint (InferiorHandle *handle, guint32 bhandle)
{
	I386BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock (handle->bpm);
	breakpoint = (I386BreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (handle->bpm, bhandle);
	if (!breakpoint) {
		mono_debugger_breakpoint_manager_unlock (handle->bpm);
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;
	}

	result = do_enable (handle, breakpoint);
	mono_debugger_breakpoint_manager_unlock (handle->bpm);
	return result;
}

static ServerCommandError
server_ptrace_disable_breakpoint (InferiorHandle *handle, guint32 bhandle)
{
	I386BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock (handle->bpm);
	breakpoint = (I386BreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (handle->bpm, bhandle);
	if (!breakpoint) {
		mono_debugger_breakpoint_manager_unlock (handle->bpm);
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;
	}

	result = do_disable (handle, breakpoint);
	mono_debugger_breakpoint_manager_unlock (handle->bpm);
	return result;
}

static ServerCommandError
server_ptrace_enable_all_breakpoints (InferiorHandle *handle)
{
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_disable_all_breakpoints (InferiorHandle *handle)
{
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_get_breakpoints (InferiorHandle *handle, guint32 *count, guint32 **retval)
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
server_ptrace_get_target_info (InferiorHandle *handle, guint32 *target_int_size,
			       guint32 *target_long_size, guint32 *target_address_size)
{
	*target_int_size = sizeof (guint32);
	*target_long_size = sizeof (guint64);
	*target_address_size = sizeof (void *);

	return COMMAND_ERROR_NONE;
}

static gboolean
do_dispatch (InferiorHandle *handle, int status, ServerStatusMessageType *type, guint64 *arg,
	     guint64 *data1, guint64 *data2)
{
	if (WIFSTOPPED (status)) {
		guint64 callback_arg, retval, retval2;
		ChildStoppedAction action = server_ptrace_child_stopped
			(handle, WSTOPSIG (status), &callback_arg, &retval, &retval2);

		switch (action) {
		case STOP_ACTION_SEND_STOPPED:
			*type = MESSAGE_CHILD_STOPPED;
			if (WSTOPSIG (status) == SIGTRAP)
				*arg = 0;
			else
				*arg = WSTOPSIG (status);
			return TRUE;

		case STOP_ACTION_BREAKPOINT_HIT:
			*type = MESSAGE_CHILD_HIT_BREAKPOINT;
			*arg = (int) retval;
			return TRUE;

		case STOP_ACTION_CALLBACK:
			*type = MESSAGE_CHILD_CALLBACK;
			*arg = callback_arg;
			*data1 = retval;
			*data2 = retval2;
			return TRUE;

		default:
			g_assert_not_reached ();
		}
	} else if (WIFEXITED (status)) {
		*type = MESSAGE_CHILD_EXITED;
		*arg = WEXITSTATUS (status);
		return TRUE;
	} else if (WIFSIGNALED (status)) {
		*type = MESSAGE_CHILD_SIGNALED;
		*arg = WTERMSIG (status);
		return TRUE;
	}

	g_warning (G_STRLOC ": Got unknown waitpid() result: %d", status);
	return FALSE;
}

static int
do_wait (InferiorHandle *handle)
{
	int ret, status = 0;
	sigset_t mask, oldmask;

	sigemptyset (&mask);
	sigaddset (&mask, SIGCHLD);
	sigaddset (&mask, SIGINT);

	sigprocmask (SIG_BLOCK, &mask, &oldmask);

 again:
	ret = waitpid (handle->pid, &status, WUNTRACED | WNOHANG | __WALL | __WCLONE);
	if (ret < 0) {
		g_warning (G_STRLOC ": Can't waitpid (%d): %s", handle->pid, g_strerror (errno));
		status = -1;
		goto out;
	} else if (ret) {
		goto out;
	}

	sigsuspend (&oldmask);
	goto again;

 out:
	sigprocmask (SIG_SETMASK, &oldmask, NULL);
	return status;
}

static void
server_ptrace_wait (InferiorHandle *handle, ServerStatusMessageType *type, guint64 *arg,
		    guint64 *data1, guint64 *data2)
{
	int status;

	do {
		status = do_wait (handle);
		if (status == -1)
			return;

	} while (!do_dispatch (handle, status, type, arg, data1, data2));
}

static InferiorHandle *
server_ptrace_initialize (BreakpointManager *bpm)
{
	InferiorHandle *handle = g_new0 (InferiorHandle, 1);

	handle->bpm = bpm;
	return handle;
}

static void
setup_inferior (InferiorHandle *handle)
{
	gchar *filename = g_strdup_printf ("/proc/%d/mem", handle->pid);
	sigset_t mask;

	sigemptyset (&mask);
	sigaddset (&mask, SIGINT);
	pthread_sigmask (SIG_BLOCK, &mask, NULL);

	do_wait (handle);

	handle->mem_fd = open64 (filename, O_RDONLY);

	if (handle->mem_fd < 0)
		g_error (G_STRLOC ": Can't open (%s): %s", filename, g_strerror (errno));

	g_free (filename);

	if (get_registers (handle, &handle->current_regs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't get registers");
	if (get_fp_registers (handle, &handle->current_fpregs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't get fp registers");
}

static void
child_setup_func (gpointer data)
{
	if (ptrace (PTRACE_TRACEME, getpid ()))
		g_error (G_STRLOC ": Can't PTRACE_TRACEME: %s", g_strerror (errno));
}

static ServerCommandError
server_ptrace_spawn (InferiorHandle *handle, const gchar *working_directory, gchar **argv, gchar **envp,
		     gboolean search_path, gint *child_pid, gint *standard_input, gint *standard_output,
		     gint *standard_error, GError **error)
{
	GSpawnFlags flags = G_SPAWN_DO_NOT_REAP_CHILD;

	if (search_path)
		flags |= G_SPAWN_SEARCH_PATH;

	if (!g_spawn_async (working_directory, argv, envp, flags, child_setup_func, NULL, child_pid, error))
		return COMMAND_ERROR_FORK;

	handle->pid = *child_pid;
	setup_inferior (handle);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_attach (InferiorHandle *handle, int pid)
{
	if (ptrace (PTRACE_ATTACH, pid))
		return COMMAND_ERROR_FORK;

	handle->pid = pid;

	setup_inferior (handle);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_get_registers (InferiorHandle *handle, guint32 count, guint32 *registers, guint64 *values)
{
	int i;

	for (i = 0; i < count; i++) {
		switch (registers [i]) {
		case EBX:
			values [i] = (guint32) handle->current_regs.ebx;
			break;
		case ECX:
			values [i] = (guint32) handle->current_regs.ecx;
			break;
		case EDX:
			values [i] = (guint32) handle->current_regs.edx;
			break;
		case ESI:
			values [i] = (guint32) handle->current_regs.esi;
			break;
		case EDI:
			values [i] = (guint32) handle->current_regs.edi;
			break;
		case EBP:
			values [i] = (guint32) handle->current_regs.ebp;
			break;
		case EAX:
			values [i] = (guint32) handle->current_regs.eax;
			break;
		case DS:
			values [i] = (guint32) handle->current_regs.ds;
			break;
		case ES:
			values [i] = (guint32) handle->current_regs.es;
			break;
		case FS:
			values [i] = (guint32) handle->current_regs.fs;
			break;
		case GS:
			values [i] = (guint32) handle->current_regs.gs;
			break;
		case ORIG_EAX:
			values [i] = (guint32) handle->current_regs.orig_eax;
			break;
		case EIP:
			values [i] = (guint32) handle->current_regs.eip;
			break;
		case CS:
			values [i] = (guint32) handle->current_regs.cs;
			break;
		case EFL:
			values [i] = (guint32) handle->current_regs.eflags;
			break;
		case UESP:
			values [i] = (guint32) handle->current_regs.esp;
			break;
		case SS:
			values [i] = (guint32) handle->current_regs.ss;
			break;
		default:
			return COMMAND_ERROR_UNKNOWN_REGISTER;
		}
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_set_registers (InferiorHandle *handle, guint32 count, guint32 *registers, guint64 *values)
{
	ServerCommandError result;
	struct user_regs_struct regs;
	int i;

	result = get_registers (handle, &regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	for (i = 0; i < count; i++) {
		switch (registers [i]) {
		case EBX:
			regs.ebx = values [i];
			break;
		case ECX:
			regs.ecx = values [i];
			break;
		case EDX:
			regs.edx = values [i];
			break;
		case ESI:
			regs.esi = values [i];
			break;
		case EDI:
			regs.edi = values [i];
			break;
		case EBP:
			regs.ebp = values [i];
			break;
		case EAX:
			regs.eax = values [i];
			break;
		case DS:
			regs.ds = values [i];
			break;
		case ES:
			regs.es = values [i];
			break;
		case FS:
			regs.fs = values [i];
			break;
		case GS:
			regs.gs = values [i];
			break;
		case ORIG_EAX:
			regs.orig_eax = values [i];
			break;
		case EIP:
			regs.eip = values [i];
			break;
		case CS:
			regs.cs = values [i];
			break;
		case EFL:
			regs.eflags = values [i];
			break;
		case UESP:
			regs.esp = values [i];
			break;
		case SS:
			regs.ss = values [i];
			break;
		default:
			return COMMAND_ERROR_UNKNOWN_REGISTER;
		}
	}

	return set_registers (handle, &regs);
}

static ServerCommandError
server_ptrace_get_frame (InferiorHandle *handle, guint32 eip, guint32 esp, guint32 ebp,
			 guint32 *retaddr, guint32 *frame)
{
	ServerCommandError result;
	guint32 value;

	result = server_ptrace_peek_word (handle, eip, &value);
	if (result != COMMAND_ERROR_NONE)
		return result;

	if ((value == 0xec8b5590) || (value == 0xec8b55cc) ||
	    ((value & 0xffffff) == 0xec8b55) || ((value & 0xffffff) == 0xe58955)) {
		result = server_ptrace_peek_word (handle, esp, &value);
		if (result != COMMAND_ERROR_NONE)
			return result;

		*retaddr = value;
		*frame = ebp;
		return COMMAND_ERROR_NONE;
	}

	result = server_ptrace_peek_word (handle, eip - 1, &value);
	if (result != COMMAND_ERROR_NONE)
		return result;

	if (((value & 0xffffff) == 0xec8b55) || ((value & 0xffffff) == 0xe58955)) {
		result = server_ptrace_peek_word (handle, esp + 4, &value);
		if (result != COMMAND_ERROR_NONE)
			return result;

		*retaddr = value;
		*frame = ebp;
		return COMMAND_ERROR_NONE;
	}

	result = server_ptrace_peek_word (handle, ebp, frame);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = server_ptrace_peek_word (handle, ebp + 4, retaddr);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_get_ret_address (InferiorHandle *handle, guint64 *retval)
{
	ServerCommandError result;
	guint32 retaddr, frame;

	result = server_ptrace_get_frame (handle, handle->current_regs.eip, handle->current_regs.esp,
					  handle->current_regs.ebp, &retaddr, &frame);
	if (result != COMMAND_ERROR_NONE)
		return result;

	*retval = retaddr;
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_get_backtrace (InferiorHandle *handle, gint32 max_frames, guint64 stop_address,
			     guint32 *count, StackFrame **data)
{
	GArray *frames = g_array_new (FALSE, FALSE, sizeof (StackFrame));
	ServerCommandError result;
	guint32 address, frame;
	StackFrame sframe;
	int i;

	sframe.address = (guint32) handle->current_regs.eip;
	sframe.params_address = sframe.locals_address = (guint32) handle->current_regs.ebp;

	g_array_append_val (frames, sframe);

	if (handle->current_regs.ebp == 0)
		goto out;

	result = server_ptrace_get_frame (handle, handle->current_regs.eip, handle->current_regs.esp,
					  handle->current_regs.ebp, &address, &frame);
	if (result != COMMAND_ERROR_NONE)
		goto out;

	while (frame != 0) {
		if ((max_frames >= 0) && (frames->len >= max_frames))
			break;

		if (address == stop_address)
			goto out;

		sframe.address = address;
		sframe.params_address = sframe.locals_address = frame;

		g_array_append_val (frames, sframe);

		result = server_ptrace_peek_word (handle, frame + 4, &address);
		if (result != COMMAND_ERROR_NONE)
			goto out;

		result = server_ptrace_peek_word (handle, frame, &frame);
		if (result != COMMAND_ERROR_NONE)
			goto out;
	}

	goto out;

 out:
	*count = frames->len;
	*data = g_new0 (StackFrame, frames->len);
	for (i = 0; i < frames->len; i++)
		(*data)[i] = g_array_index (frames, StackFrame, i);
	g_array_free (frames, FALSE);
	return result;
}

static ServerCommandError
server_ptrace_stop (InferiorHandle *handle)
{
	kill (handle->pid, SIGSTOP);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_set_signal (InferiorHandle *handle, guint32 sig, guint32 send_it)
{
	if (send_it)
		kill (handle->pid, sig);
	else
		handle->last_signal = sig;
	return COMMAND_ERROR_NONE;
}

/*
 * Method VTable for this backend.
 */
InferiorInfo i386_linux_ptrace_inferior = {
	server_ptrace_initialize,
	server_ptrace_spawn,
	server_ptrace_attach,
	server_ptrace_detach,
	server_ptrace_finalize,
	server_ptrace_wait,
	server_ptrace_get_target_info,
	server_ptrace_continue,
	server_ptrace_step,
	server_ptrace_get_pc,
	server_ptrace_current_insn_is_bpt,
	server_ptrace_read_data,
	server_ptrace_write_data,
	server_ptrace_call_method,
	server_ptrace_call_method_1,
	server_ptrace_call_method_invoke,
	server_ptrace_insert_breakpoint,
	server_ptrace_insert_hw_breakpoint,
	server_ptrace_remove_breakpoint,
	server_ptrace_enable_breakpoint,
	server_ptrace_disable_breakpoint,
	server_ptrace_enable_all_breakpoints,
	server_ptrace_disable_all_breakpoints,
	server_ptrace_get_breakpoints,
	server_ptrace_get_registers,
	server_ptrace_set_registers,
	server_ptrace_get_backtrace,
	server_ptrace_get_ret_address,
	server_ptrace_stop,
	server_ptrace_set_signal
};
