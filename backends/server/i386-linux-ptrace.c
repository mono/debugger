#include <server.h>
#include <sys/ptrace.h>
#include <asm/ptrace.h>
#include <asm/user.h>
#include <signal.h>
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
	long call_address;
	guint64 callback_argument;
	struct user_regs_struct *saved_regs;
	struct user_i387_struct *saved_fpregs;
};

void
server_ptrace_traceme (int pid)
{
	if (ptrace (PTRACE_TRACEME, pid))
		g_error (G_STRLOC ": Can't PTRACE_TRACEME: %s", g_strerror (errno));
}

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

InferiorHandle *
server_ptrace_get_handle (int pid)
{
	InferiorHandle *handle;

	handle = g_new0 (InferiorHandle, 1);
	handle->pid = pid;

	return handle;
}

InferiorHandle *
server_ptrace_attach (int pid)
{
	InferiorHandle *handle;

	if (ptrace (PTRACE_ATTACH, pid))
		g_error (G_STRLOC ": Can't attach to process %d: %s", pid, g_strerror (errno));

	handle = g_new0 (InferiorHandle, 1);
	handle->pid = pid;

	return handle;
}

ServerCommandError
server_ptrace_continue (InferiorHandle *handle)
{
	errno = 0;
	if (ptrace (PTRACE_CONT, handle->pid, NULL, NULL)) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;

		g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
server_ptrace_step (InferiorHandle *handle)
{
	errno = 0;
	if (ptrace (PTRACE_SINGLESTEP, handle->pid, NULL, NULL)) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;

		g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
server_ptrace_detach (InferiorHandle *handle)
{
	if (ptrace (PTRACE_DETACH, handle->pid, NULL, NULL)) {
		g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
server_get_program_counter (InferiorHandle *handle, guint64 *pc)
{
	ServerCommandError result;
	struct user_regs_struct regs;

	result = get_registers (handle, &regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	*pc = regs.eip;
	return result;
}

ServerCommandError
server_ptrace_read_data (InferiorHandle *handle, guint64 start, guint32 size, gpointer buffer)
{
	int *ptr = buffer;
	int addr = (int) start;

	while (size) {
		int word;

		errno = 0;
		word = ptrace (PTRACE_PEEKDATA, handle->pid, addr);
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;
		else if (errno) {
			g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
			return COMMAND_ERROR_UNKNOWN;
		}

		if (size >= sizeof (int)) {
			*ptr++ = word;
			addr += sizeof (int);
			size -= sizeof (int);
		} else {
			memcpy (ptr, &word, size);
			size = 0;
		}
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
server_ptrace_write_data (InferiorHandle *handle, guint64 start, guint32 size, gpointer buffer)
{
	int *ptr = buffer;
	int addr = start;

	if (size % sizeof (int))
		return COMMAND_ERROR_ALIGNMENT;

	while (size) {
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

	return COMMAND_ERROR_NONE;
}

/*
 * This method is highly architecture and specific.
 * It will only work on the i386.
 */

ServerCommandError
server_ptrace_call_method (InferiorHandle *handle, guint64 method_address,
			   guint64 method_argument, guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	struct user_regs_struct regs;
	long new_esp, call_disp;

	guint8 code[] = { 0x68, 0x00, 0x00, 0x00, 0x00, 0x68, 0x00, 0x00,
			  0x00, 0x00, 0xe8, 0x00, 0x00, 0x00, 0x00, 0xcc };
	int size = sizeof (code);

	if (handle->saved_regs)
		return COMMAND_ERROR_RECURSIVE_CALL;

	result = get_registers (handle, &regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	new_esp = regs.esp - size;

	handle->saved_regs = g_memdup (&regs, sizeof (regs));
	handle->call_address = new_esp + 16;
	handle->callback_argument = callback_argument;

	handle->saved_fpregs = g_malloc (sizeof (struct user_i387_struct));
	result = get_fp_registers (handle, handle->saved_fpregs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	call_disp = (int) method_address - new_esp;

	*((guint32 *) (code+1)) = method_argument >> 32;
	*((guint32 *) (code+6)) = method_argument & 0xffffffff;
	*((guint32 *) (code+11)) = call_disp - 15;

	result = server_ptrace_write_data (handle, new_esp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	regs.esp = regs.eip = new_esp;

	result = set_registers (handle, &regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return server_ptrace_continue (handle);
}

gboolean
server_handle_child_stopped (InferiorHandle *handle, int stopsig,
			     guint64 *callback_arg, guint64 *retval)
{
	struct user_regs_struct regs;

	if (!handle->call_address)
		return FALSE;

	if (get_registers (handle, &regs) != COMMAND_ERROR_NONE)
		return FALSE;

	if (!handle->call_address)
		return FALSE;

	if (handle->call_address != regs.eip)
		return FALSE;

	if (set_registers (handle, handle->saved_regs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't restore registers after returning from a call");

	if (set_fp_registers (handle, handle->saved_fpregs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't restore FP registers after returning from a call");

	*callback_arg = handle->callback_argument;
	*retval = (((guint64) regs.ecx) << 32) + ((gulong) regs.eax);

	g_free (handle->saved_regs);
	g_free (handle->saved_fpregs);

	handle->saved_regs = NULL;
	handle->saved_fpregs = NULL;
	handle->call_address = 0;
	handle->callback_argument = 0;

	return TRUE;
}
