#include <server.h>
#include <sys/ptrace.h>
#include <asm/ptrace.h>
#include <asm/user.h>
#include <sys/wait.h>
#include <signal.h>
#include <unistd.h>
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
	ChildExitedFunc child_exited_cb;
	ChildMessageFunc child_message_cb;
	ChildCallbackFunc child_callback_cb;
	guint64 callback_argument;
	struct user_regs_struct *saved_regs;
	struct user_i387_struct *saved_fpregs;
	GPtrArray *breakpoints;
	GHashTable *breakpoint_hash;
};

static int last_breakpoint_id = 0;

typedef struct {
	int id;
	char saved_insn;
	guint64 address;
} BreakPointInfo;

typedef struct {
	GSource source;
	InferiorHandle *handle;
	int status;
} PTraceSource;

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

	return handle;
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

	*pc = regs.eip;
	return result;
}

static ServerCommandError
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

	result = server_ptrace_read_data (handle, addr, 4, &temp);
	if (result != COMMAND_ERROR_NONE)
		return result;
	memcpy (&temp, ptr, size);

	return server_ptrace_write_data (handle, addr, 4, &temp);
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

	result = server_ptrace_write_data (handle, new_esp, size, code);
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
	int i;

	if (!handle->breakpoints)
		return FALSE;

	for (i = 0; i < handle->breakpoints->len; i++) {
		BreakPointInfo *info = g_ptr_array_index (handle->breakpoints, i);

		if (info->address == address) {
			*retval = info->id;
			return TRUE;
		}
	}

	return FALSE;
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

	if (!handle->call_address || handle->call_address != regs.eip)
		return STOP_ACTION_SEND_STOPPED;

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
server_ptrace_insert_breakpoint (InferiorHandle *handle, guint64 address, guint32 *bhandle)
{
	BreakPointInfo *breakpoint = g_new0 (BreakPointInfo, 1);
	ServerCommandError result;
	char bopcode = 0xcc;

	if (!handle->breakpoints) {
		handle->breakpoints = g_ptr_array_new ();
		handle->breakpoint_hash = g_hash_table_new (NULL, NULL);
	}

	breakpoint->address = address;
	breakpoint->id = ++last_breakpoint_id;
	result = server_ptrace_read_data (handle, address, 1, &breakpoint->saved_insn);
	if (result != COMMAND_ERROR_NONE) {
		g_free (breakpoint);
		return result;
	}

	result = server_ptrace_write_data (handle, address, 1, &bopcode);
	if (result != COMMAND_ERROR_NONE) {
		g_free (breakpoint);
		return result;
	}

	g_ptr_array_add (handle->breakpoints, breakpoint);
	g_hash_table_insert (handle->breakpoint_hash, GUINT_TO_POINTER (breakpoint->id), breakpoint);
	*bhandle = breakpoint->id;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_remove_breakpoint (InferiorHandle *handle, guint32 bhandle)
{
	BreakPointInfo *breakpoint;
	ServerCommandError result;

	if (!handle->breakpoints)
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;

	breakpoint = g_hash_table_lookup (handle->breakpoint_hash, GUINT_TO_POINTER (bhandle));
	if (!breakpoint)
		return COMMAND_ERROR_NO_SUCH_BREAKPOINT;

	result = server_ptrace_write_data (handle, breakpoint->address, 1, &breakpoint->saved_insn);
	if (result != COMMAND_ERROR_NONE)
		return result;

	g_hash_table_remove (handle->breakpoint_hash, GUINT_TO_POINTER (bhandle));
	g_ptr_array_remove_fast (handle->breakpoints, breakpoint);
	g_free (breakpoint);

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
		BreakPointInfo *info = g_ptr_array_index (handle->breakpoints, i);

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
	} else if (WIFEXITED (status))
		handle->child_message_cb (MESSAGE_CHILD_EXITED, WEXITSTATUS (status));
	else if (WIFSIGNALED (status))
		handle->child_message_cb (MESSAGE_CHILD_SIGNALED, WTERMSIG (status));
	else
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
		g_warning (G_STRLOC ": Can't waitpid (%d): %s", handle->pid, g_strerror (errno));
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

/*
 * Method VTable for this backend.
 */
InferiorInfo i386_linux_ptrace_inferior = {
	server_ptrace_spawn,
	server_ptrace_attach,
	server_ptrace_detach,
	server_ptrace_get_g_source,
	server_ptrace_wait,
	server_ptrace_get_target_info,
	server_ptrace_continue,
	server_ptrace_step,
	server_ptrace_get_pc,
	server_ptrace_read_data,
	server_ptrace_write_data,
	server_ptrace_call_method,
	server_ptrace_child_stopped,
	server_ptrace_insert_breakpoint,
	server_ptrace_remove_breakpoint,
	server_ptrace_get_breakpoints
};
