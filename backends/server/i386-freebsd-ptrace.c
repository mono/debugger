static ServerCommandError
get_registers (InferiorHandle *handle, INFERIOR_REGS_TYPE *regs)
{
	if (ptrace (PT_GETREGS, handle->pid, (caddr_t) regs, 0) != 0) {
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
set_registers (InferiorHandle *handle, INFERIOR_REGS_TYPE *regs)
{
	if (ptrace (PT_SETREGS, handle->pid, (caddr_t) regs, 0) != 0) {
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
get_fp_registers (InferiorHandle *handle, INFERIOR_FPREGS_TYPE *regs)
{
	if (ptrace (PT_GETFPREGS, handle->pid, (caddr_t) regs, 0) != 0) {
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
set_fp_registers (InferiorHandle *handle, INFERIOR_FPREGS_TYPE *regs)
{
	if (ptrace (PT_SETFPREGS, handle->pid, (caddr_t) regs, 0) != 0) {
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
server_ptrace_read_data (InferiorHandle *handle, guint64 start, guint32 size, gpointer buffer)
{
	guint32 old_size = size;
        int *ptr = buffer;
        int addr = (int) start;

        while (size) {
                int word;

                errno = 0;
                word = ptrace (PT_READ_D, handle->pid, (gpointer) addr, 0);
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

	debugger_arch_i386_remove_breakpoints_from_target_memory (handle, start, old_size, buffer);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_set_dr (InferiorHandle *handle, int regnum, unsigned long value)
{
	return COMMAND_ERROR_UNKNOWN;
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
	ret = waitpid (handle->pid, &status, WUNTRACED | WNOHANG);
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
setup_inferior (InferiorHandle *handle)
{
	sigset_t mask;

	sigemptyset (&mask);
	sigaddset (&mask, SIGINT);
	sigprocmask (SIG_BLOCK, &mask, NULL);

	do_wait (handle);

	if (get_registers (handle, &handle->current_regs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't get registers");
	if (get_fp_registers (handle, &handle->current_fpregs) != COMMAND_ERROR_NONE)
		g_error (G_STRLOC ": Can't get fp registers");
}
