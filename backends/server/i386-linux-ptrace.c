static ServerCommandError
_server_ptrace_get_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs)
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

static ServerCommandError
_server_ptrace_set_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs)
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

static ServerCommandError
_server_ptrace_get_fp_registers (InferiorHandle *inferior, INFERIOR_FPREGS_TYPE *regs)
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

static ServerCommandError
_server_ptrace_set_fp_registers (InferiorHandle *inferior, INFERIOR_FPREGS_TYPE *regs)
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

static ServerCommandError
server_ptrace_read_memory (ServerHandle *handle, guint64 start,
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
			g_message (G_STRLOC ": %lx - can't read target memory of %d at "
				   "address %08Lx : %s", pthread_self (),
				   handle->inferior->pid, start, g_strerror (errno));
			return COMMAND_ERROR_MEMORY_ACCESS;
		}

		size -= ret;
		ptr += ret;
	}

	i386_arch_remove_breakpoints_from_target_memory (handle, start, old_size, buffer);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
_server_ptrace_set_dr (InferiorHandle *handle, int regnum, unsigned long value)
{
	errno = 0;
	ptrace (PTRACE_POKEUSER, handle->pid, offsetof (struct user, u_debugreg [regnum]), value);
	if (errno) {
		g_message (G_STRLOC ": %d - %d - %s", handle->pid, regnum, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

GStaticMutex wait_mutex = G_STATIC_MUTEX_INIT;
GStaticMutex wait_mutex_2 = G_STATIC_MUTEX_INIT;

static int
do_wait (int pid, guint32 *status)
{
	int ret;

	ret = waitpid (pid, status, WUNTRACED | __WALL | __WCLONE);
	if (ret < 0) {
		if (errno == EINTR)
			return 0;
		g_warning (G_STRLOC ": Can't waitpid for %d: %s", pid, g_strerror (errno));
		return -1;
	}

	return ret;
}

static int first_status = 0;
static int first_ret = 0;

static int global_pid = 0;
static int stop_requested = 0;
static int stop_status = 0;

static guint32
server_ptrace_global_wait (guint32 *status_ret)
{
	int ret, status;

	if (first_status) {
		*status_ret = first_status;
		first_status = 0;
		return first_ret;
	}

	g_static_mutex_lock (&wait_mutex);
	ret = do_wait (-1, &status);
	if (ret <= 0)
		goto out;

#if DEBUG_WAIT
	g_message (G_STRLOC ": global wait finished: %d - %x - %d",
		   ret, status, stop_requested);
#endif

	g_static_mutex_lock (&wait_mutex_2);
	if (ret == stop_requested) {
		stop_status = status;
		g_static_mutex_unlock (&wait_mutex_2);
		g_static_mutex_unlock (&wait_mutex);
		return 0;
	}
	g_static_mutex_unlock (&wait_mutex_2);

	*status_ret = status;
 out:
	g_static_mutex_unlock (&wait_mutex);
	return ret;
}

static ServerCommandError
server_ptrace_stop (ServerHandle *handle)
{
	ServerCommandError result;

	/*
	 * Try to get the thread's registers.  If we suceed, then it's already stopped
	 * and still alive.
	 */
	result = i386_arch_get_registers (handle);
	if (result == COMMAND_ERROR_NONE)
		return COMMAND_ERROR_ALREADY_STOPPED;

	if (syscall (__NR_tkill, handle->inferior->pid, SIGSTOP)) {
		/*
		 * It's already dead.
		 */
		if (errno == ESRCH)
			return COMMAND_ERROR_NO_INFERIOR;
		else
			return COMMAND_ERROR_UNKNOWN;
	}

	return COMMAND_ERROR_NONE;
}

static void
server_ptrace_global_stop (void)
{
	kill (global_pid, SIGSTOP);
}

static ServerCommandError
server_ptrace_stop_and_wait (ServerHandle *handle, guint32 *status)
{
	ServerCommandError result;
	int ret;

	/*
	 * Try to get the thread's registers.  If we suceed, then it's already stopped
	 * and still alive.
	 */
#if DEBUG_WAIT
	g_message (G_STRLOC ": stop and wait %d", handle->inferior->pid);
#endif
	g_static_mutex_lock (&wait_mutex_2);
	result = server_ptrace_stop (handle);
	if (result != COMMAND_ERROR_NONE) {
#if DEBUG_WAIT
		g_message (G_STRLOC ": %d - cannot stop %d", handle->inferior->pid, result);
#endif
		g_static_mutex_unlock (&wait_mutex_2);
		return result;
	}

	stop_requested = handle->inferior->pid;
	g_static_mutex_unlock (&wait_mutex_2);

#if DEBUG_WAIT
	g_message (G_STRLOC ": %d - sent SIGSTOP", handle->inferior->pid);
#endif

	g_static_mutex_lock (&wait_mutex);
#if DEBUG_WAIT
	g_message (G_STRLOC ": %d - got stop status %x", handle->inferior->pid, stop_status);
#endif
	if (stop_status) {
		*status = stop_status;
		stop_requested = stop_status = 0;
		g_static_mutex_unlock (&wait_mutex);
		return COMMAND_ERROR_NONE;
	}

	stop_requested = stop_status = 0;

	do {
#if DEBUG_WAIT
		g_message (G_STRLOC ": %d - waiting", handle->inferior->pid);
#endif
		ret = do_wait (handle->inferior->pid, status);
#if DEBUG_WAIT
		g_message (G_STRLOC ": %d - done waiting %d, %x",
			   handle->inferior->pid, ret, status);
#endif
	} while (ret == 0);
	g_static_mutex_unlock (&wait_mutex);

	/*
	 * Should never happen.
	 */
	if (ret < 0)
		return COMMAND_ERROR_NO_INFERIOR;

	/*
	 * We expect a SIGSTOP, so don't explicitly report it.
	 */
	if (WIFSTOPPED (*status) && (WSTOPSIG (*status) == SIGSTOP))
		*status = 0;

	return COMMAND_ERROR_NONE;
}

static void
_server_ptrace_setup_inferior (ServerHandle *handle, gboolean is_main)
{
	gchar *filename = g_strdup_printf ("/proc/%d/mem", handle->inferior->pid);
	int status, ret;

	do {
		ret = do_wait (handle->inferior->pid, &status);
	} while (ret == 0);

	if (is_main) {
		g_assert (ret == handle->inferior->pid);
		first_status = status;
		first_ret = ret;
	}

	if (i386_arch_get_registers (handle) != COMMAND_ERROR_NONE)
		g_error ("Can't get registers");

	handle->inferior->mem_fd = open64 (filename, O_RDONLY);

	if (handle->inferior->mem_fd < 0)
		g_error (G_STRLOC ": Can't open (%s): %s", filename, g_strerror (errno));

	g_free (filename);

	if (!is_main) {
		handle->inferior->tid = i386_arch_get_tid (handle);
	}
}

static gboolean
_server_ptrace_setup_thread_manager (ServerHandle *handle)
{
	int flags = PTRACE_O_TRACEFORK | PTRACE_O_TRACEVFORKDONE | PTRACE_O_TRACECLONE;

	if (ptrace (PTRACE_SETOPTIONS, handle->inferior->pid, 0, flags)) {
		g_warning (G_STRLOC ": Can't PTRACE_SETOPTIONS %d: %s",
			   handle->inferior->pid, g_strerror (errno));
		return FALSE;
	}

	global_pid = handle->inferior->pid;

	return TRUE;
}

static ServerCommandError
server_ptrace_get_signal_info (ServerHandle *handle, SignalInfo *sinfo)
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
