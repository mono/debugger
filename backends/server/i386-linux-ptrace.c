#include <server.h>
#include <sys/ptrace.h>
#include <asm/ptrace.h>
#include <errno.h>

struct InferiorHandle
{
	int pid;
};

void
server_ptrace_traceme (int pid)
{
	if (ptrace (PTRACE_TRACEME, pid))
		g_error (G_STRLOC ": Can't PTRACE_TRACEME: %s", g_strerror (errno));
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
	if (ptrace (PTRACE_DETACH, handle->pid)) {
		g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
server_get_program_counter (InferiorHandle *handle, guint64 *pc)
{
	*pc = ptrace (PTRACE_PEEKUSER, handle->pid, sizeof (long) * EIP);
	if (errno == ESRCH)
		return COMMAND_ERROR_NOT_STOPPED;
	else if (errno) {
		g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
server_ptrace_read_data (InferiorHandle *handle, guint64 start, guint32 size, gpointer buffer)
{
	int *ptr = buffer;

	while (size) {
		int word;

		word = ptrace (PTRACE_PEEKDATA, handle->pid, start);
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;
		else if (errno) {
			g_message (G_STRLOC ": %d - %s", handle->pid, g_strerror (errno));
			return COMMAND_ERROR_UNKNOWN;
		}

		if (size >= sizeof (int)) {
			*ptr++ = word;
			start += sizeof (int);
			size -= sizeof (int);
		} else {
			memcpy (ptr, &word, size);
			size = 0;
		}
	}

	return COMMAND_ERROR_NONE;
}

