#include <config.h>
#include <server.h>
#include <signal.h>
#include <unistd.h>
#include <sys/time.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <arpa/inet.h>
#include <netdb.h>
#include <Debugger.h>

CORBA_ORB orb;
Debugger_Manager debugger_manager;
static int the_socket;

struct InferiorHandle {
	Debugger_Thread debugger_thread;
};

#define CHECK_RESULT		\
	if (ev._major == CORBA_SYSTEM_EXCEPTION) {			\
		CORBA_exception_free (&ev);				\
		return COMMAND_ERROR_IO;				\
	} else if (ev._major) {						\
		Debugger_Error *ex = CORBA_exception_value (&ev);	\
		ServerCommandError result = ex->condition;		\
		CORBA_exception_free (&ev);				\
		return result;						\
	}

static int
setup_corba (void)
{
	const char *argv[3] = {
		"remoting-client", "--ORBIIOPIPv4=1", NULL };
        int argc = 2;
	CORBA_Environment ev;
	struct sockaddr_in name;
	struct hostent *hostinfo;
	gchar *remote_var, *port_pos;
	int len, port = 0;
	gchar *ior;

	the_socket = socket (PF_INET, SOCK_STREAM, 0);
	g_assert (the_socket >= 0);

	remote_var = g_strdup (g_getenv ("MONO_DEBUGGER_REMOTE"));
	g_assert (remote_var);

	port_pos = strchr (remote_var, ':');
	if (port_pos) {
		*port_pos++ = 0;
		port = atoi (port_pos);
	}
	if (!port)
		port = 40857;

	hostinfo = gethostbyname (remote_var);
	if (!hostinfo) {
		g_warning (G_STRLOC ": Can't lookup host %s", remote_var);
		return -1;
	}

	name.sin_family = AF_INET;
	name.sin_port = htons (port);
	name.sin_addr = * (struct in_addr *) hostinfo->h_addr;

	g_assert (hostinfo);

	if (connect (the_socket, &name, sizeof (name))) {
		g_warning (G_STRLOC ": Can't conntext: %s", g_strerror (errno));
		return -1;
	}

	if (recv (the_socket, &len, 4, 0) != 4) {
		g_warning (G_STRLOC ": recv failed: %s", g_strerror (errno));
		return -1;
	}

	len = ntohl (len);

	ior = g_malloc0 (len + 1);
	if (recv (the_socket, ior, len, 0) != len) {
		g_warning (G_STRLOC ": recv failed: %s", g_strerror (errno));
		return -1;
	}

	CORBA_exception_init (&ev);
	orb = CORBA_ORB_init (&argc, (char **) argv, "orbit-local-orb", &ev);

	debugger_manager = CORBA_ORB_string_to_object (orb, ior, &ev);
	if (ev._major) {
		g_warning (G_STRLOC ": Can't bind to object `%s'", ior);
		return -1;
	}

	CORBA_exception_free (&ev);
	return 0;
}

static ServerHandle *
remote_server_initialize (BreakpointManager *breakpoint_manager)
{
	ServerHandle *handle = g_new0 (ServerHandle, 1);

	handle->bpm = breakpoint_manager;

	if (setup_corba ())
		return NULL;

	return handle;
}

static Debugger_stringList *
allocate_stringlist (const gchar **array)
{
	Debugger_stringList *list;
	const gchar **ptr;
	int i, count;

	list = Debugger_stringList__alloc ();
	if (!array)
		return list;

	for (ptr = array, count = 0; *ptr; ptr++, count++)
		;

	list->_buffer = Debugger_stringList_allocbuf (count);
	list->_maximum = list->_length = count;

	for (i = 0; i < count; i++)
		list->_buffer [i] = (CORBA_string) array [i];

	return list;
}

static ServerCommandError
remote_server_spawn (ServerHandle *handle, const gchar *working_directory,
		     const gchar **argv, const gchar **envp, gint *child_pid,
		     ChildOutputFunc stdout_handler, ChildOutputFunc stderr_handler,
		     gchar **error)
{
	Debugger_stringList *corba_argv, *corba_envp;
	CORBA_Environment ev;
	CORBA_long pid;

	if (handle->inferior)
		return COMMAND_ERROR_ALREADY_HAVE_INFERIOR;

	handle->inferior = g_new0 (InferiorHandle, 1);

	CORBA_exception_init (&ev);

	corba_argv = allocate_stringlist (argv);
	corba_envp = allocate_stringlist (envp);

	handle->inferior->debugger_thread = Debugger_Manager_Spawn (
		debugger_manager, working_directory, corba_argv, corba_envp, &pid, &ev);

	if (!ev._major) {
		*child_pid = pid;
		*error = NULL;
		return COMMAND_ERROR_NONE;
	}

	*error = g_strdup (CORBA_exception_id (&ev));
	CORBA_exception_free (&ev);

	return COMMAND_ERROR_FORK;
}

static ServerCommandError
remote_server_attach (ServerHandle *handle, guint32 pid, guint32 *tid)
{
	CORBA_Environment ev;

	if (handle->inferior)
		return COMMAND_ERROR_ALREADY_HAVE_INFERIOR;

	handle->inferior = g_new0 (InferiorHandle, 1);

	CORBA_exception_init (&ev);
	handle->inferior->debugger_thread = Debugger_Manager_Attach (
		debugger_manager, pid, tid, &ev);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static void
remote_server_finalize (ServerHandle *handle)
{
	CORBA_Environment ev;

	CORBA_exception_init (&ev);
	CORBA_Object_release (handle->inferior->debugger_thread, &ev);
	CORBA_exception_free (&ev);

	g_free (handle->inferior);
	g_free (handle);
}

static ServerStatusMessageType
remote_server_dispatch_event (ServerHandle *handle, guint64 status, guint64 *arg,
			      guint64 *data1, guint64 *data2)
{
	CORBA_Environment ev;
	CORBA_long ret;

	CORBA_exception_init (&ev);
	ret = Debugger_Thread_DispatchEvent (handle->inferior->debugger_thread, status, arg, data1, data2, &ev);
	g_assert (!ev._major);
	CORBA_exception_free (&ev);

	return ret;
}

static ServerCommandError
remote_server_get_target_info (guint32 *target_int_size, guint32 *target_long_size,
			       guint32 *target_address_size)
{
	CORBA_Environment ev;
	Debugger_TargetInfo info;

	CORBA_exception_init (&ev);
	info = Debugger_Manager_GetTargetInfo (debugger_manager, &ev);
	CHECK_RESULT;
	CORBA_exception_free (&ev);

	*target_int_size = info.IntSize;
	*target_long_size = info.LongSize;
	*target_address_size = info.AddressSize;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_get_pc (ServerHandle *handle, guint64 *pc)
{
	CORBA_Environment ev;

	CORBA_exception_init (&ev);
	*pc = Debugger_Thread_GetFrame (handle->inferior->debugger_thread, &ev);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_current_insn_is_bpt (ServerHandle *handle, guint32 *is_breakpoint)
{
	CORBA_Environment ev;
	CORBA_boolean ret;

	CORBA_exception_init (&ev);
	ret = Debugger_Thread_CurrentInsnIsBreakpoint (handle->inferior->debugger_thread, &ev);
	CHECK_RESULT;

	*is_breakpoint = ret;
	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_step (ServerHandle *handle)
{
	CORBA_Environment ev;

	CORBA_exception_init (&ev);
	Debugger_Thread_Step (handle->inferior->debugger_thread, &ev);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_continue (ServerHandle *handle)
{
	CORBA_Environment ev;

	CORBA_exception_init (&ev);
	Debugger_Thread_Continue (handle->inferior->debugger_thread, &ev);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_detach (ServerHandle *handle)
{
	CORBA_Environment ev;

	CORBA_exception_init (&ev);
	Debugger_Thread_Detach (handle->inferior->debugger_thread, &ev);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_peek_word (ServerHandle *handle, guint64 start, guint32 *word)
{
	CORBA_Environment ev;

	CORBA_exception_init (&ev);
	*word = Debugger_Thread_PeekWord (handle->inferior->debugger_thread, start, &ev);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_read_memory (ServerHandle *handle, guint64 start, guint32 size, gpointer data)
{
	CORBA_Environment ev;
	Debugger_Blob *blob;

	CORBA_exception_init (&ev);
	blob = Debugger_Thread_ReadMemory (handle->inferior->debugger_thread, start, size, &ev);
	CHECK_RESULT;

	memcpy (data, blob->_buffer, size);
	CORBA_free (blob);

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_write_memory (ServerHandle *handle, guint64 start, guint32 size, gconstpointer data)
{
	CORBA_Environment ev;
	Debugger_Blob *blob;

	blob = Debugger_Blob__alloc ();
	blob->_length = blob->_maximum = size;
	blob->_buffer = Debugger_Blob_allocbuf (size);
	memcpy (blob->_buffer, data, size);

	CORBA_exception_init (&ev);
	Debugger_Thread_WriteMemory (handle->inferior->debugger_thread, start, blob, &ev);
	CORBA_free (blob);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_call_method (ServerHandle *handle, guint64 method_address,
			   guint64 method_argument1, guint64 method_argument2,
			   guint64 callback_argument)
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

static ServerCommandError
remote_server_call_method_1 (ServerHandle *handle, guint64 method_address,
			     guint64 method_argument, const gchar *string_argument,
			     guint64 callback_argument)
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

static ServerCommandError
remote_server_call_method_invoke (ServerHandle *handle, guint64 invoke_method,
				  guint64 method_argument, guint64 object_argument,
				  guint32 num_params, guint64 *param_data,
				  guint64 callback_argument)
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

static ServerCommandError
remote_server_insert_breakpoint (ServerHandle *handle, guint64 address, guint32 *breakpoint)
{
	CORBA_Environment ev;
	CORBA_long ret;

	CORBA_exception_init (&ev);
	ret = Debugger_Thread_InsertBreakpoint (handle->inferior->debugger_thread, address, &ev);
	CHECK_RESULT;

	*breakpoint = ret;
	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_insert_hw_breakpoint (ServerHandle *handle, guint32 idx, guint64 address,
				    guint32 *breakpoint)
{
	CORBA_Environment ev;

	CORBA_exception_init (&ev);
	*breakpoint = Debugger_Thread_InsertHardwareBreakpoint (handle->inferior->debugger_thread, idx, address, &ev);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_remove_breakpoint (ServerHandle *handle, guint32 breakpoint)
{
	CORBA_Environment ev;

	CORBA_exception_init (&ev);
	Debugger_Thread_RemoveBreakpoint (handle->inferior->debugger_thread, breakpoint, &ev);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_enable_breakpoint (ServerHandle *handle, guint32 breakpoint)
{
	CORBA_Environment ev;

	CORBA_exception_init (&ev);
	Debugger_Thread_EnableBreakpoint (handle->inferior->debugger_thread, breakpoint, &ev);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_disable_breakpoint (ServerHandle *handle, guint32 breakpoint)
{
	CORBA_Environment ev;

	CORBA_exception_init (&ev);
	Debugger_Thread_DisableBreakpoint (handle->inferior->debugger_thread, breakpoint, &ev);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_get_registers (ServerHandle *handle, guint32 count, guint32 *registers,
			     guint64 *values)
{
	CORBA_Environment ev;
	Debugger_RegisterList *list;
	int i;

	list = Debugger_RegisterList__alloc ();
	list->_length = list->_maximum = count;
	list->_buffer = Debugger_RegisterList_allocbuf (count);

	for (i = 0; i < count; i++)
		list->_buffer [i].Index = registers [i];

	CORBA_exception_init (&ev);
	Debugger_Thread_GetRegisters (handle->inferior->debugger_thread, list, &ev);
	if (ev._major)
		CORBA_free (list);
	CHECK_RESULT;

	for (i = 0; i < count; i++)
		values [i] = list->_buffer [i].Value;

	CORBA_free (list);
	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_set_registers (ServerHandle *handle, guint32 count, guint32 *registers,
			     guint64 *values)
{
	CORBA_Environment ev;
	Debugger_RegisterList *list;
	int i;

	list = Debugger_RegisterList__alloc ();
	list->_length = list->_maximum = count;
	list->_buffer = Debugger_RegisterList_allocbuf (count);

	for (i = 0; i < count; i++) {
		list->_buffer [i].Index = registers [i];
		list->_buffer [i].Value = values [i];
	}

	CORBA_exception_init (&ev);
	Debugger_Thread_GetRegisters (handle->inferior->debugger_thread, list, &ev);
	CORBA_free (list);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_get_backtrace (ServerHandle *handle, gint32 max_frames, guint64 stop_address,
			     guint32 *count, StackFrame **frames_ret)
{
	CORBA_Environment ev;
	Debugger_StackFrameList *list;
	StackFrame *frames;
	int i;

	CORBA_exception_init (&ev);
	list = Debugger_Thread_GetBacktrace (handle->inferior->debugger_thread, max_frames, stop_address, &ev);
	CHECK_RESULT;

	*count = list->_length;
	frames = g_new0 (StackFrame, list->_length);
	for (i = 0; i < list->_length; i++) {
		frames [i].address = list->_buffer [i].Frame;
		frames [i].frame_address = list->_buffer [i].FrameAddress;
	}
	*frames_ret = frames;

	CORBA_free (list);
	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_get_ret_address (ServerHandle *handle, guint64 *retval)
{
	CORBA_Environment ev;

	CORBA_exception_init (&ev);
	*retval = Debugger_Thread_GetReturnAddress (handle->inferior->debugger_thread, &ev);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_stop (ServerHandle *handle)
{
	CORBA_Environment ev;

	CORBA_exception_init (&ev);
	Debugger_Thread_Stop (handle->inferior->debugger_thread, &ev);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
remote_server_stop_and_wait (ServerHandle *handle, guint32 *status)
{
	CORBA_Environment ev;

	CORBA_exception_init (&ev);
	*status = Debugger_Thread_StopAndWait (handle->inferior->debugger_thread, &ev);
	CHECK_RESULT;

	CORBA_exception_free (&ev);
	return COMMAND_ERROR_NONE;
}

static guint32
remote_server_global_wait (guint64 *status)
{
	guint32 result, status_ret, command = htonl (1);

	if (send (the_socket, &command, 4, 0) != 4) {
		g_warning (G_STRLOC ": Send failed: %s", g_strerror (errno));
		return -1;
	}

	if (recv (the_socket, &result, 4, 0) != 4) {
		g_warning (G_STRLOC ": Recv failed: %s", g_strerror (errno));
		return -1;
	}

	if (recv (the_socket, &status_ret, 4, 0) != 4) {
		g_warning (G_STRLOC ": Recv failed: %s", g_strerror (errno));
		return -1;
	}

	*status = ntohl (status_ret);

	return ntohl (result);
}

static void
remote_server_global_stop (void)
{
	guint32 result, command = htonl (2);

	if (send (the_socket, &command, 4, 0) != 4) {
		g_warning (G_STRLOC ": Send failed: %s", g_strerror (errno));
		return -1;
	}
}

static ServerCommandError
remote_server_set_signal (ServerHandle *handle, guint32 sig, guint32 send_it)
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

static ServerCommandError
remote_server_kill (ServerHandle *handle)
{
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

static ServerCommandError
remote_server_get_signal_info (ServerHandle *handle, SignalInfo *sinfo)
{
	sinfo->sigkill = SIGKILL;
	sinfo->sigstop = SIGSTOP;
	sinfo->sigint = SIGINT;
	sinfo->sigchld = SIGCHLD;
	sinfo->sigprof = SIGPROF;

#if defined(__POWERPC__)
	sinfo->sigpwr = 0;
	sinfo->sigxcpu = 0;
#else
	sinfo->sigpwr = SIGPWR;
	sinfo->sigxcpu = SIGXCPU;
#endif

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
	return COMMAND_ERROR_NOT_IMPLEMENTED;
}

InferiorVTable remote_client_inferior = {
	remote_server_initialize,
	remote_server_spawn,
	remote_server_attach,
	remote_server_detach,
	remote_server_finalize,
	remote_server_global_wait,
	remote_server_stop_and_wait,
	remote_server_dispatch_event,
	remote_server_get_target_info,
	remote_server_continue,
	remote_server_step,
	remote_server_get_pc,
	remote_server_current_insn_is_bpt,
	remote_server_peek_word,
	remote_server_read_memory,
	remote_server_write_memory,
	remote_server_call_method,
	remote_server_call_method_1,
	remote_server_call_method_invoke,
	remote_server_insert_breakpoint,
	remote_server_insert_hw_breakpoint,
	remote_server_remove_breakpoint,
	remote_server_enable_breakpoint,
	remote_server_disable_breakpoint,
	NULL,
	remote_server_get_registers,
	remote_server_set_registers,
	remote_server_get_backtrace,
	remote_server_get_ret_address,
	remote_server_stop,
	remote_server_global_stop,
	remote_server_set_signal,
	remote_server_kill,
	remote_server_get_signal_info
};
