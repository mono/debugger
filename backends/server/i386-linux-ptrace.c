ServerCommandError
_mono_debugger_server_get_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs)
{
	if (ptrace (PT_GETREGS, inferior->pid, NULL, regs) != 0) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;
		else if (errno) {
			g_message (G_STRLOC ": %d - %s", inferior->pid, g_strerror (errno));
			return COMMAND_ERROR_UNKNOWN;
		}
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
_mono_debugger_server_set_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs)
{
	if (ptrace (PT_SETREGS, inferior->pid, NULL, regs) != 0) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;
		else if (errno) {
			g_message (G_STRLOC ": %d - %s", inferior->pid, g_strerror (errno));
			return COMMAND_ERROR_UNKNOWN;
		}
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
_mono_debugger_server_get_fp_registers (InferiorHandle *inferior, INFERIOR_FPREGS_TYPE *regs)
{
	if (ptrace (PT_GETFPREGS, inferior->pid, NULL, regs) != 0) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;
		else if (errno) {
			g_message (G_STRLOC ": %d - %s", inferior->pid, g_strerror (errno));
			return COMMAND_ERROR_UNKNOWN;
		}
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
_mono_debugger_server_set_fp_registers (InferiorHandle *inferior, INFERIOR_FPREGS_TYPE *regs)
{
	if (ptrace (PT_SETFPREGS, inferior->pid, NULL, regs) != 0) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;
		else if (errno) {
			g_message (G_STRLOC ": %d - %s", inferior->pid, g_strerror (errno));
			return COMMAND_ERROR_UNKNOWN;
		}
	}

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mono_debugger_server_read_memory (ServerHandle *handle, guint64 start,
				  guint32 size, gpointer buffer)
{
	guint8 *ptr = buffer;
	guint32 old_size = size;

	while (size) {
		int ret = pread64 (handle->inferior->mem_fd, ptr, size, start);
		if (ret < 0) {
			if (errno == EINTR)
				continue;
			else if (errno == EIO)
				return COMMAND_ERROR_MEMORY_ACCESS;
			g_warning (G_STRLOC ": Can't read target memory at address %08Lx: %s",
				   start, g_strerror (errno));
			return COMMAND_ERROR_UNKNOWN;
		}

		size -= ret;
		ptr += ret;
	}

	i386_arch_remove_breakpoints_from_target_memory (handle, start, old_size, buffer);

	return COMMAND_ERROR_NONE;
}

ServerCommandError
_mono_debugger_server_set_dr (InferiorHandle *handle, int regnum, unsigned long value)
{
	errno = 0;
	ptrace (PTRACE_POKEUSER, handle->pid, offsetof (struct user, u_debugreg [regnum]), value);
	if (errno) {
		g_message (G_STRLOC ": %d - %d - %s", handle->pid, regnum, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

int
_mono_debugger_server_wait (InferiorHandle *inferior)
{
	int ret, status = 0, signo = 0;

 again:
	if (!inferior->is_thread)
		check_io (inferior);
	/* Check whether the target changed its state in the meantime. */
	ret = waitpid (inferior->pid, &status, WUNTRACED | WNOHANG | __WALL | __WCLONE);
	if (ret < 0) {
		g_warning (G_STRLOC ": Can't waitpid (%d): %s", inferior->pid, g_strerror (errno));
		status = -1;
		goto out;
	} else if (ret) {
		goto out;
	}

	/*
	 * Wait until the target changed its state (in this case, we receive a SIGCHLD), I/O is
	 * possible or another event occured.
	 *
	 * Each time I/O is possible, emit the corresponding events.  Note that we must read the
	 * target's stdout/stderr as soon as it becomes available since otherwise the target may
	 * block in __libc_write().
	 */
	GC_start_blocking ();
	sigwait (&mono_debugger_signal_mask, &signo);
	GC_end_blocking ();
	goto again;

 out:
	return status;
}

void
_mono_debugger_server_setup_inferior (ServerHandle *handle)
{
	gchar *filename = g_strdup_printf ("/proc/%d/mem", handle->inferior->pid);

	_mono_debugger_server_wait (handle->inferior);

	handle->inferior->mem_fd = open64 (filename, O_RDONLY);

	if (handle->inferior->mem_fd < 0)
		g_error (G_STRLOC ": Can't open (%s): %s", filename, g_strerror (errno));

	g_free (filename);

	i386_arch_get_registers (handle);
}

ServerCommandError
mono_debugger_server_get_signal_info (ServerHandle *handle, SignalInfo *sinfo)
{
	sinfo->sigkill = SIGKILL;
	sinfo->sigstop = SIGSTOP;
	sinfo->sigint = SIGINT;
	sinfo->sigchld = SIGCHLD;
	sinfo->sigprof = SIGPROF;
	sinfo->sigpwr = SIGPWR;
	sinfo->sigxcpu = SIGXCPU;

#if 0
	sinfo->thread_abort = 34;
	sinfo->thread_restart = 33;
	sinfo->thread_debug = 32;
	sinfo->mono_thread_debug = -1;
#else
	sinfo->thread_abort = 33;
	sinfo->thread_restart = 32;
	sinfo->thread_debug = 34;
	sinfo->mono_thread_debug = 34;
#endif

	return COMMAND_ERROR_NONE;
}
