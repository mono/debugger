#define _GNU_SOURCE
#include <server.h>
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
	long call_address;
	ChildExitedFunc child_exited_cb;
	ChildMessageFunc child_message_cb;
	ChildCallbackFunc child_callback_cb;
	guint64 callback_argument;
	struct user_regs_struct *saved_regs;
	struct user_i387_struct *saved_fpregs;
	GPtrArray *breakpoints;
	GHashTable *breakpoint_hash;
	GHashTable *breakpoint_by_addr;
};

static int last_breakpoint_id = 0;

typedef struct {
	int id;
	int enabled;
	char saved_insn;
	guint32 address;
} BreakpointInfo;

typedef struct {
	GSource source;
	InferiorHandle *handle;
	int status;
} PTraceSource;

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
setup_inferior (InferiorHandle *handle)
{
	gchar *filename = g_strdup_printf ("/proc/%d/mem", handle->pid);
	handle->mem_fd = open64 (filename, O_RDONLY);

	if (handle->mem_fd < 0)
		g_error (G_STRLOC ": Can't open (%s): %s", filename, g_strerror (errno));

	g_free (filename);
}

static InferiorHandle *
server_ptrace_attach (int pid, ChildExitedFunc child_exited, ChildMessageFunc child_message,
		      ChildCallbackFunc child_callback)
{
	InferiorHandle *handle;

	if (ptrace (PTRACE_ATTACH, pid))
		g_error (G_STRLOC ": Can't attach to process %d: %s", pid, g_strerror (errno));

	handle = g_new0 (InferiorHandle, 1);
	handle->pid = pid;
	handle->child_exited_cb = child_exited;
	handle->child_message_cb = child_message;
	handle->child_callback_cb = child_callback;

	setup_inferior (handle);

	return handle;
}

static void
server_ptrace_finalize (InferiorHandle *handle)
{
	if (handle->pid)
		ptrace (PTRACE_KILL, handle->pid, NULL, NULL);

	g_free (handle->saved_regs);
	g_free (handle->saved_fpregs);
	if (handle->breakpoints)
		g_ptr_array_free (handle->breakpoints, TRUE);
	if (handle->breakpoint_hash)
		g_hash_table_destroy (handle->breakpoint_hash);
	if (handle->breakpoint_by_addr)
		g_hash_table_destroy (handle->breakpoint_by_addr);
	g_free (handle);
}

static ServerCommandError
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

static ServerCommandError
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
	ServerCommandError result;
	struct user_regs_struct regs;

	result = get_registers (handle, &regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	*pc = (guint32) regs.eip;
	return result;
}

static ServerCommandError
server_ptrace_current_insn_is_bpt (InferiorHandle *handle, guint32 *is_breakpoint)
{
	ServerCommandError result;
	struct user_regs_struct regs;

	result = get_registers (handle, &regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	if (!handle->breakpoints) {
		*is_breakpoint = FALSE;
		return COMMAND_ERROR_NONE;
	}

	if (g_hash_table_lookup (handle->breakpoint_by_addr, GUINT_TO_POINTER (regs.eip)))
		*is_breakpoint = TRUE;
	else
		*is_breakpoint = FALSE;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_peek_word (InferiorHandle *handle, guint64 start, int *retval)
{
	errno = 0;
	*retval = ptrace (PTRACE_PEEKDATA, handle->pid, start, NULL);
	if (errno) {
		g_message (G_STRLOC ": %d - %08Lx - %s", handle->pid, start, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

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

	result = server_ptrace_write_data (handle, (unsigned long) new_esp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	regs.esp = regs.eip = new_esp;

	result = set_registers (handle, &regs);
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
	struct user_regs_struct regs;
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

	result = get_registers (handle, &regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	new_esp = regs.esp - size;

	handle->saved_regs = g_memdup (&regs, sizeof (regs));
	handle->call_address = new_esp + 21;
	handle->callback_argument = callback_argument;

	handle->saved_fpregs = g_malloc (sizeof (struct user_i387_struct));
	result = get_fp_registers (handle, handle->saved_fpregs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	call_disp = (int) method_address - new_esp;

	*((guint32 *) (code+1)) = new_esp + 21;
	*((guint32 *) (code+6)) = method_argument >> 32;
	*((guint32 *) (code+11)) = method_argument & 0xffffffff;
	*((guint32 *) (code+16)) = call_disp - 20;

	result = server_ptrace_write_data (handle, (unsigned long) new_esp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	regs.esp = regs.eip = new_esp;

	result = set_registers (handle, &regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return server_ptrace_continue (handle);
}

static ServerCommandError
server_ptrace_call_method_invoke (InferiorHandle *handle, guint64 invoke_method,
				  guint64 method_argument, guint64 object_argument,
				  guint32 num_params, guint64 *param_data, guint64 *exc_address,
				  guint64 callback_argument)
{
	ServerCommandError result = COMMAND_ERROR_NONE;
	struct user_regs_struct regs;
	long new_esp, call_disp;
	int i;

	static guint8 static_code[] = { 0x90, 0x68, 0x00, 0x00, 0x00, 0x00, 0x68, 0x00,
					0x00, 0x00, 0x00, 0x68, 0x00, 0x00, 0x00, 0x00,
					0x68, 0x00, 0x00, 0x00, 0x00, 0xe8, 0x00, 0x00,
					0x00, 0x00, 0xe8, 0x00, 0x00, 0x00, 0x00, 0x5a,
					0x8d, 0x92, 0x00, 0x00, 0x00, 0x00, 0x8b, 0x12,
					0xcc };
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

	result = get_registers (handle, &regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	new_esp = regs.esp - size;

	handle->saved_regs = g_memdup (&regs, sizeof (regs));
	handle->call_address = new_esp + static_size;
	handle->callback_argument = callback_argument;

	handle->saved_fpregs = g_malloc (sizeof (struct user_i387_struct));
	result = get_fp_registers (handle, handle->saved_fpregs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	call_disp = (int) invoke_method - new_esp;

	*((guint32 *) (code+2)) = new_esp + static_size + (num_params + 1) * 4;
	*((guint32 *) (code+7)) = new_esp + static_size;
	*((guint32 *) (code+12)) = object_argument;
	*((guint32 *) (code+17)) = method_argument;
	*((guint32 *) (code+22)) = call_disp - 26;
	*((guint32 *) (code+34)) = 10 + (num_params + 1) * 4;

	g_message (G_STRLOC ": %x - %x", (guint32) invoke_method, handle->call_address);

	result = server_ptrace_write_data (handle, (unsigned long) new_esp, size, code);
	if (result != COMMAND_ERROR_NONE)
		return result;

	regs.esp = regs.eip = new_esp;

	result = set_registers (handle, &regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return server_ptrace_continue (handle);
}

static gboolean
check_breakpoint (InferiorHandle *handle, long address, guint64 *retval)
{
	BreakpointInfo *info;

	if (!handle->breakpoints)
		return FALSE;

	info = g_hash_table_lookup (handle->breakpoint_by_addr, GUINT_TO_POINTER (address));
	if (!info || !info->enabled)
		return FALSE;

	*retval = info->id;
	return TRUE;
}

static ChildStoppedAction
server_ptrace_child_stopped (InferiorHandle *handle, int stopsig,
			     guint64 *callback_arg, guint64 *retval)
{
	struct user_regs_struct regs;

	if (get_registers (handle, &regs) != COMMAND_ERROR_NONE)
		return STOP_ACTION_SEND_STOPPED;

	if (check_breakpoint (handle, regs.eip - 1, retval)) {
		regs.eip--;
		set_registers (handle, &regs);
		return STOP_ACTION_BREAKPOINT_HIT;
	}

	if (!handle->call_address || handle->call_address != regs.eip) {
		int code;

		if (stopsig != SIGTRAP)
			return STOP_ACTION_SEND_STOPPED;

		if (server_ptrace_peek_word (handle, (guint32) (regs.eip - 1), &code) != COMMAND_ERROR_NONE)
			return STOP_ACTION_SEND_STOPPED;

		if ((code & 0xff) == 0xcc) {
			*retval = 0;
			return STOP_ACTION_BREAKPOINT_HIT;
		}

		return STOP_ACTION_SEND_STOPPED;
	}

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

	return STOP_ACTION_CALLBACK;
}

static ServerCommandError
do_enable (InferiorHandle *handle, BreakpointInfo *breakpoint)
{
	ServerCommandError result;
	char bopcode = 0xcc;

	if (breakpoint->enabled)
		return COMMAND_ERROR_NONE;

	result = server_ptrace_read_data (handle, breakpoint->address, 1, &breakpoint->saved_insn);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = server_ptrace_write_data (handle, breakpoint->address, 1, &bopcode);
	if (result != COMMAND_ERROR_NONE)
		return result;

	breakpoint->enabled = TRUE;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
do_disable (InferiorHandle *handle, BreakpointInfo *breakpoint)
{
	ServerCommandError result;

	if (!breakpoint->enabled)
		return COMMAND_ERROR_NONE;

	result = server_ptrace_write_data (handle, breakpoint->address, 1, &breakpoint->saved_insn);
	if (result != COMMAND_ERROR_NONE)
		return result;

	breakpoint->enabled = FALSE;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_insert_breakpoint (InferiorHandle *handle, guint64 address, guint32 *bhandle)
{
	BreakpointInfo *breakpoint = g_new0 (BreakpointInfo, 1);
	ServerCommandError result;

	if (!handle->breakpoints) {
		handle->breakpoints = g_ptr_array_new ();
		handle->breakpoint_hash = g_hash_table_new (NULL, NULL);
		handle->breakpoint_by_addr = g_hash_table_new (NULL, NULL);
	}

	breakpoint->address = (guint32) address;
	breakpoint->id = ++last_breakpoint_id;

	result = do_enable (handle, breakpoint);
	if (result != COMMAND_ERROR_NONE) {
		g_free (breakpoint);
		return result;
	}

	g_ptr_array_add (handle->breakpoints, breakpoint);
	g_hash_table_insert (handle->breakpoint_hash, GUINT_TO_POINTER (breakpoint->id), breakpoint);
	g_hash_table_insert (handle->breakpoint_by_addr, GUINT_TO_POINTER (breakpoint->address), breakpoint);
	*bhandle = breakpoint->id;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_remove_breakpoint (InferiorHandle *handle, guint32 bhandle)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	if (!handle->breakpoints)
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;

	breakpoint = g_hash_table_lookup (handle->breakpoint_hash, GUINT_TO_POINTER (bhandle));
	if (!breakpoint)
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;

	result = do_disable (handle, breakpoint);
	if (result != COMMAND_ERROR_NONE)
		return result;

	g_hash_table_remove (handle->breakpoint_hash, GUINT_TO_POINTER (bhandle));
	g_hash_table_remove (handle->breakpoint_by_addr, GUINT_TO_POINTER (breakpoint->address));
	g_ptr_array_remove_fast (handle->breakpoints, breakpoint);
	g_free (breakpoint);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_enable_breakpoint (InferiorHandle *handle, guint32 bhandle)
{
	BreakpointInfo *breakpoint;

	if (!handle->breakpoints)
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;

	breakpoint = g_hash_table_lookup (handle->breakpoint_hash, GUINT_TO_POINTER (bhandle));
	if (!breakpoint)
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;

	return do_enable (handle, breakpoint);
}

static ServerCommandError
server_ptrace_disable_breakpoint (InferiorHandle *handle, guint32 bhandle)
{
	BreakpointInfo *breakpoint;

	if (!handle->breakpoints)
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;

	breakpoint = g_hash_table_lookup (handle->breakpoint_hash, GUINT_TO_POINTER (bhandle));
	if (!breakpoint)
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;

	return do_disable (handle, breakpoint);
}

static void
do_enable_cb (gpointer key, gpointer value, gpointer user_data)
{
	do_enable ((InferiorHandle *) user_data, (BreakpointInfo *) value);
}

static void
do_disable_cb (gpointer key, gpointer value, gpointer user_data)
{
	do_disable ((InferiorHandle *) user_data, (BreakpointInfo *) value);
}

static ServerCommandError
server_ptrace_enable_all_breakpoints (InferiorHandle *handle)
{
	if (!handle->breakpoints)
		return COMMAND_ERROR_NONE;

	g_hash_table_foreach (handle->breakpoint_hash, do_enable_cb, handle);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_disable_all_breakpoints (InferiorHandle *handle)
{
	if (!handle->breakpoints)
		return COMMAND_ERROR_NONE;

	g_hash_table_foreach (handle->breakpoint_hash, do_disable_cb, handle);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_get_breakpoints (InferiorHandle *handle, guint32 *count, guint32 **breakpoints)
{
	int i;

	if (!handle->breakpoints)
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;

	*count = handle->breakpoints->len;
	*breakpoints = g_new0 (guint32, handle->breakpoints->len);

	for (i = 0; i < handle->breakpoints->len; i++) {
		BreakpointInfo *info = g_ptr_array_index (handle->breakpoints, i);

		(*breakpoints) [i] = info->id;
	}

	return COMMAND_ERROR_NONE;	
}

static void
child_setup_func (gpointer data)
{
	if (ptrace (PTRACE_TRACEME, getpid ()))
		g_error (G_STRLOC ": Can't PTRACE_TRACEME: %s", g_strerror (errno));
}

static InferiorHandle *
server_ptrace_spawn (const gchar *working_directory, gchar **argv, gchar **envp, gboolean search_path,
		     ChildExitedFunc child_exited, ChildMessageFunc child_message,
		     ChildCallbackFunc child_callback, gint *child_pid, gint *standard_input,
		     gint *standard_output, gint *standard_error, GError **error)
{
	GSpawnFlags flags = G_SPAWN_DO_NOT_REAP_CHILD;
	InferiorHandle *handle;

	if (search_path)
		flags |= G_SPAWN_SEARCH_PATH;

	if (!g_spawn_async (working_directory, argv, envp, flags, child_setup_func, NULL, child_pid, error))
		return NULL;

	handle = g_new0 (InferiorHandle, 1);
	handle->pid = *child_pid;
	handle->child_exited_cb = child_exited;
	handle->child_message_cb = child_message;
	handle->child_callback_cb = child_callback;

	setup_inferior (handle);

	return handle;
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

static void
do_dispatch (InferiorHandle *handle, int status)
{
	if (WIFSTOPPED (status)) {
		guint64 callback_arg, retval;
		ChildStoppedAction action = server_ptrace_child_stopped
			(handle, WSTOPSIG (status), &callback_arg, &retval);

		switch (action) {
		case STOP_ACTION_SEND_STOPPED:
			if (WSTOPSIG (status) == SIGTRAP)
				handle->child_message_cb (MESSAGE_CHILD_STOPPED, 0);
			else
				handle->child_message_cb (MESSAGE_CHILD_STOPPED, WSTOPSIG (status));
			break;

		case STOP_ACTION_BREAKPOINT_HIT:
			handle->child_message_cb (MESSAGE_CHILD_HIT_BREAKPOINT, (int) retval);
			break;

		case STOP_ACTION_CALLBACK:
			handle->child_callback_cb (callback_arg, retval);
			break;

		default:
			g_assert_not_reached ();
		}
	} else if (WIFEXITED (status)) {
		handle->child_message_cb (MESSAGE_CHILD_EXITED, WEXITSTATUS (status));
		handle->child_exited_cb ();
	} else if (WIFSIGNALED (status)) {
		handle->child_message_cb (MESSAGE_CHILD_SIGNALED, WTERMSIG (status));
		handle->child_exited_cb ();
	} else
		g_warning (G_STRLOC ": Got unknown waitpid() result: %d", status);
}

static gboolean 
source_check (GSource *source)
{
	PTraceSource *psource = (PTraceSource *) source;
	InferiorHandle *handle = psource->handle;
	int ret, status;

	if (psource->status)
		return TRUE;

	/* If the child stopped in the meantime. */
	ret = waitpid (handle->pid, &status, WNOHANG | WUNTRACED);

	if (ret < 0) {
		g_error (G_STRLOC ": Can't waitpid (%d): %s", handle->pid, g_strerror (errno));
		return FALSE;
	} else if (ret == 0)
		return FALSE;

	psource->status = status;

	// do_dispatch (handle, status);

	return TRUE;
}

static gboolean 
source_prepare (GSource *source, gint *timeout)
{
	PTraceSource *psource = (PTraceSource *) source;

	*timeout = -1;

	psource->status = 0;

	return source_check (source);
}

static gboolean
source_dispatch (GSource *source, GSourceFunc callback, gpointer user_data)
{
	PTraceSource *psource = (PTraceSource *) source;

	if (callback) {
		g_warning ("Ooops, you must not set a callback on this source!");
		return FALSE;
	}

	do_dispatch (psource->handle, psource->status);

	return TRUE;
}

GSourceFuncs mono_debugger_source_funcs =
{
	source_prepare,
	source_check,
	source_dispatch,
	NULL
};

static GSource *
server_ptrace_get_g_source (InferiorHandle *handle)
{
	GSource *source = g_source_new (&mono_debugger_source_funcs, sizeof (PTraceSource));
	PTraceSource *psource = (PTraceSource *) source;

	psource->handle = handle;

	return source;
}

static void
server_ptrace_wait (InferiorHandle *handle)
{
	int ret, status;

	/* Wait until the child stops. */
	ret = waitpid (handle->pid, &status, WUNTRACED);

	if (ret < 0) {
		g_warning (G_STRLOC ": Can't waitpid (%d): %s", handle->pid, g_strerror (errno));
		return;
	} else if (ret == 0)
		return;

	do_dispatch (handle, status);
}

static ServerCommandError
server_ptrace_get_registers (InferiorHandle *handle, guint32 count, guint32 *registers, guint64 *values)
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
			values [i] = (guint32) regs.ebx;
			break;
		case ECX:
			values [i] = (guint32) regs.ecx;
			break;
		case EDX:
			values [i] = (guint32) regs.edx;
			break;
		case ESI:
			values [i] = (guint32) regs.esi;
			break;
		case EDI:
			values [i] = (guint32) regs.edi;
			break;
		case EBP:
			values [i] = (guint32) regs.ebp;
			break;
		case EAX:
			values [i] = (guint32) regs.eax;
			break;
		case DS:
			values [i] = (guint32) regs.ds;
			break;
		case ES:
			values [i] = (guint32) regs.es;
			break;
		case FS:
			values [i] = (guint32) regs.fs;
			break;
		case GS:
			values [i] = (guint32) regs.gs;
			break;
		case ORIG_EAX:
			values [i] = (guint32) regs.orig_eax;
			break;
		case EIP:
			values [i] = (guint32) regs.eip;
			break;
		case CS:
			values [i] = (guint32) regs.cs;
			break;
		case EFL:
			values [i] = (guint32) regs.eflags;
			break;
		case UESP:
			values [i] = (guint32) regs.esp;
			break;
		case SS:
			values [i] = (guint32) regs.ss;
			break;
		default:
			return COMMAND_ERROR_UNKNOWN_REGISTER;
		}
	}

	return COMMAND_ERROR_NONE;
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
	struct user_regs_struct regs;
	guint32 retaddr, frame;

	result = get_registers (handle, &regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = server_ptrace_get_frame (handle, regs.eip, regs.esp, regs.ebp, &retaddr, &frame);
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
	struct user_regs_struct regs;
	guint32 address, frame;
	StackFrame sframe;
	int i;

	result = get_registers (handle, &regs);
	if (result != COMMAND_ERROR_NONE)
		goto out;

	sframe.address = (guint32) regs.eip;
	sframe.params_address = sframe.locals_address = (guint32) regs.ebp;

	g_array_append_val (frames, sframe);

	if (regs.ebp == 0)
		goto out;

	result = server_ptrace_get_frame (handle, regs.eip, regs.esp, regs.ebp,
					  &address, &frame);
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

/*
 * Method VTable for this backend.
 */
InferiorInfo i386_linux_ptrace_inferior = {
	server_ptrace_spawn,
	server_ptrace_attach,
	server_ptrace_detach,
	server_ptrace_finalize,
	server_ptrace_get_g_source,
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
	server_ptrace_remove_breakpoint,
	server_ptrace_enable_breakpoint,
	server_ptrace_disable_breakpoint,
	server_ptrace_enable_all_breakpoints,
	server_ptrace_disable_all_breakpoints,
	server_ptrace_get_breakpoints,
	server_ptrace_get_registers,
	server_ptrace_get_backtrace,
	server_ptrace_get_ret_address,
	server_ptrace_stop
};
