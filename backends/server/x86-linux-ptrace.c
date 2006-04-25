static ServerCommandError
_server_ptrace_get_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs)
{
	if (ptrace (PT_GETREGS, inferior->pid, NULL, regs) != 0) {
		if (errno == ESRCH)
			return COMMAND_ERROR_NOT_STOPPED;
		else if (errno) {
			g_message (G_STRLOC ": %d - %s", inferior->pid, g_strerror (errno));
			return COMMAND_ERROR_UNKNOWN_ERROR;
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
			return COMMAND_ERROR_UNKNOWN_ERROR;
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
			return COMMAND_ERROR_UNKNOWN_ERROR;
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
			return COMMAND_ERROR_UNKNOWN_ERROR;
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
				   handle->inferior->pid, (long long) start, g_strerror (errno));
			return COMMAND_ERROR_MEMORY_ACCESS;
		}

		size -= ret;
		ptr += ret;
	}

	x86_arch_remove_breakpoints_from_target_memory (handle, start, old_size, buffer);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
_server_ptrace_set_dr (InferiorHandle *handle, int regnum, unsigned value)
{
	errno = 0;
	ptrace (PTRACE_POKEUSER, handle->pid, offsetof (struct user, u_debugreg [regnum]), value);
	if (errno) {
		g_message (G_STRLOC ": %d - %d - %s", handle->pid, regnum, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	return COMMAND_ERROR_NONE;
}


static ServerCommandError
_server_ptrace_get_dr (InferiorHandle *handle, int regnum, unsigned *value)
{
	int ret;

	errno = 0;
	ret = ptrace (PTRACE_PEEKUSER, handle->pid, offsetof (struct user, u_debugreg [regnum]));
	if (errno) {
		g_message (G_STRLOC ": %d - %d - %s", handle->pid, regnum, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	*value = ret;
	return COMMAND_ERROR_NONE;
}

GStaticMutex wait_mutex = G_STATIC_MUTEX_INIT;
GStaticMutex wait_mutex_2 = G_STATIC_MUTEX_INIT;

static int
do_wait (int pid, guint32 *status)
{
	int ret;

#if DEBUG_WAIT
	g_message (G_STRLOC ": do_wait (%d)", pid);
#endif
	ret = waitpid (pid, status, WUNTRACED | __WALL | __WCLONE);
#if DEBUG_WAIT
	g_message (G_STRLOC ": do_wait (%d) finished: %d - %x", pid, ret, status);
#endif
	if (ret < 0) {
		if (errno == EINTR)
			return 0;
		else if (errno == ECHILD)
			return -1;
		g_warning (G_STRLOC ": Can't waitpid for %d: %s", pid, g_strerror (errno));
		return -1;
	}

	return ret;
}

static int stop_requested = 0;
static int stop_status = 0;

static guint32
server_ptrace_global_wait (guint32 *status_ret)
{
	int ret, status;

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
	result = x86_arch_get_registers (handle);
	if (result == COMMAND_ERROR_NONE)
		return COMMAND_ERROR_ALREADY_STOPPED;

	if (syscall (__NR_tkill, handle->inferior->pid, SIGSTOP)) {
		/*
		 * It's already dead.
		 */
		if (errno == ESRCH)
			return COMMAND_ERROR_NO_TARGET;
		else
			return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	return COMMAND_ERROR_NONE;
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
		return COMMAND_ERROR_NO_TARGET;

	/*
	 * We expect a SIGSTOP, so don't explicitly report it.
	 */
	if (WIFSTOPPED (*status) && (WSTOPSIG (*status) == SIGSTOP))
		*status = 0;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
_server_ptrace_setup_inferior (ServerHandle *handle)
{
	gchar *filename = g_strdup_printf ("/proc/%d/mem", handle->inferior->pid);

	handle->inferior->mem_fd = open64 (filename, O_RDONLY);

	if (handle->inferior->mem_fd < 0) {
		g_warning (G_STRLOC ": Can't open (%s): %s", filename, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	g_free (filename);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_initialize_process (ServerHandle *handle)
{
	int flags = PTRACE_O_TRACECLONE | PTRACE_O_TRACEFORK | PTRACE_O_TRACEVFORK |
		PTRACE_O_TRACEEXEC;

	if (ptrace (PTRACE_SETOPTIONS, handle->inferior->pid, 0, flags)) {
		g_warning (G_STRLOC ": Can't PTRACE_SETOPTIONS %d: %s",
			   handle->inferior->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_get_signal_info (ServerHandle *handle, SignalInfo **sinfo_out)
{
	SignalInfo *sinfo = g_new0 (SignalInfo, 1);

	sinfo->sigkill = SIGKILL;
	sinfo->sigstop = SIGSTOP;
	sinfo->sigint = SIGINT;
	sinfo->sigchld = SIGCHLD;
	sinfo->mono_thread_abort = mono_thread_get_abort_signal ();

	*sinfo_out = sinfo;

	return COMMAND_ERROR_NONE;
}

static void
server_ptrace_global_init (void)
{
	stop_requested = 0;
	stop_status = 0;
}

static ServerCommandError
server_ptrace_get_threads (ServerHandle *handle, guint32 *count, guint32 **threads)
{
	gchar *dirname = g_strdup_printf ("/proc/%d/task", handle->inferior->pid);
	const gchar *filename;
	GPtrArray *array;
	GDir *dir;
	int i;

	dir = g_dir_open (dirname, 0, NULL);
	if (!dir) {
		g_warning (G_STRLOC ": Can't get threads of %d", handle->inferior->pid);
		g_free (dirname);
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	array = g_ptr_array_new ();

	while ((filename = g_dir_read_name (dir)) != NULL) {
		gchar *endptr;
		guint32 pid;

		pid = (guint32) strtol (filename, &endptr, 10);
		if (*endptr)
			goto out_error;

		g_ptr_array_add (array, GUINT_TO_POINTER (pid));
	}

	*count = array->len;
	*threads = g_new0 (guint32, array->len);

	for (i = 0; i < array->len; i++)
		(*threads) [i] = GPOINTER_TO_UINT (g_ptr_array_index (array, i));

	g_free (dirname);
	g_dir_close (dir);
	g_ptr_array_free (array, FALSE);
	return COMMAND_ERROR_NONE;

 out_error:
	g_free (dirname);
	g_dir_close (dir);
	g_ptr_array_free (array, FALSE);
	g_warning (G_STRLOC ": Can't get threads of %d", handle->inferior->pid);
	return COMMAND_ERROR_UNKNOWN_ERROR;
}

static ServerCommandError
server_ptrace_get_application (ServerHandle *handle, gchar **exe_file, gchar **cwd,
			       guint32 **nargs, gchar ***cmdline_args)
{
	gchar *exe_filename = g_strdup_printf ("/proc/%d/exe", handle->inferior->pid);
	gchar *cwd_filename = g_strdup_printf ("/proc/%d/cwd", handle->inferior->pid);
	gchar *cmdline_filename = g_strdup_printf ("/proc/%d/cmdline", handle->inferior->pid);
	char buffer [BUFSIZ];
	GPtrArray *array;
	gchar *cmdline;
	gsize pos, len;
	int i;

	len = readlink (exe_filename, buffer, BUFSIZ);
	if (len < 0) {
		g_free (cwd_filename);
		g_free (exe_filename);
		g_free (cmdline_filename);
		g_warning (G_STRLOC ": Can't get exe file of %d", handle->inferior->pid);
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	buffer [len] = 0;
	*exe_file = g_strdup (buffer);

	len = readlink (cwd_filename, buffer, BUFSIZ);
	if (len < 0) {
		g_free (cwd_filename);
		g_free (exe_filename);
		g_free (cmdline_filename);
		g_warning (G_STRLOC ": Can't get cwd of %d", handle->inferior->pid);
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	buffer [len] = 0;
	*cwd = g_strdup (buffer);

	if (!g_file_get_contents (cmdline_filename, &cmdline, &len, NULL)) {
		g_free (cwd_filename);
		g_free (exe_filename);
		g_free (cmdline_filename);
		g_warning (G_STRLOC ": Can't get cmdline args of %d", handle->inferior->pid);
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	array = g_ptr_array_new ();

	pos = 0;
	while (pos < len) {
		g_ptr_array_add (array, cmdline + pos);
		pos += strlen (cmdline + pos) + 1;
	}

	*nargs = array->len;
	*cmdline_args = g_new0 (gchar *, array->len + 1);

	for (i = 0; i < array->len; i++)
		(*cmdline_args) [i] = g_ptr_array_index (array, i);

	g_free (cwd_filename);
	g_free (exe_filename);
	g_free (cmdline_filename);
	g_ptr_array_free (array, FALSE);
	return COMMAND_ERROR_NONE;
}
