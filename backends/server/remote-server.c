#include <stdio.h>
#include <stdlib.h>
#include <errno.h>
#include <signal.h>
#include <string.h>
#include <debugger-srv.h>
#include <sys/wait.h>
#include <netinet/in.h>
#include <pthread.h>

CORBA_ORB orb;
PortableServer_POA rootpoa;
PortableServer_POA the_poa;
Debugger_Manager debugger_manager;
PortableServer_ObjectId *the_objid;
static BreakpointManager *bpm;
static PortableServer_POAManager rootpoa_mgr;
static const char *the_ior;
static int the_port;

static GMutex *mutex;
static GCond *cond;
static GCond *ready_cond;
static GMutex *ready_mutex;

typedef struct
{
	POA_Debugger_Manager servant;
	PortableServer_POA poa;
} impl_POA_Debugger_Manager;

static gchar **
copy_stringlist (const Debugger_stringList *list)
{
	gchar **result = g_new0 (gchar *, list->_length + 1);
	int i;

	for (i = 0; i < list->_length; i++)
		result [i] = list->_buffer [i];

	return result;
}

static Debugger_Thread
do_spawn (impl_POA_Debugger_Manager *servant, const CORBA_char *working_dir,
	  const Debugger_stringList *corba_argv, const Debugger_stringList *corba_envp,
	  CORBA_long *child_pid, CORBA_Environment *ev)
{
	const gchar **argv, **envp;
	ServerCommandError result;
	Debugger_ForkFailed *ex;
	ServerHandle *handle;
	gchar *error;

	CHECK_VTABLE_RET (spawn, CORBA_OBJECT_NIL);

	argv = (const gchar **) copy_stringlist (corba_argv);
	envp = (const gchar **) copy_stringlist (corba_envp);

	handle = global_vtable->initialize (bpm);

	result = global_vtable->spawn (
		handle, working_dir, argv, envp, child_pid, NULL, NULL, &error);

	if (result != COMMAND_ERROR_NONE) {
		ex = Debugger_ForkFailed__alloc ();
		ex->message = error;

		CORBA_exception_set (ev, CORBA_USER_EXCEPTION, ex_Debugger_ForkFailed, ex);
		return CORBA_OBJECT_NIL;
	}

	return debugger_srv_start_object (handle, ev);
}

static Debugger_Thread
do_attach (impl_POA_Debugger_Manager *servant, const CORBA_long_long pid,
	   CORBA_long *tid, CORBA_Environment *ev)
{
	ServerCommandError result;
	ServerHandle *handle;

	CHECK_VTABLE_RET (attach, CORBA_OBJECT_NIL);

	handle = global_vtable->initialize (bpm);

	result = global_vtable->attach (handle, pid, tid);
	CHECK_RESULT_RET (CORBA_OBJECT_NIL);

	return debugger_srv_start_object (handle, ev);
}

static Debugger_TargetInfo
do_get_target_info (impl_POA_Debugger_Manager *servant, CORBA_Environment *ev)
{
	ServerCommandError result;
	guint32 int_size, long_size, address_size, is_bigendian;
	Debugger_TargetInfo info;

	CHECK_VTABLE_RET (get_target_info, info);
	result = global_vtable->get_target_info (
		&int_size, &long_size, &address_size, &is_bigendian);
	CHECK_RESULT_RET (info);

	info.IntSize = int_size;
	info.LongSize = long_size;
	info.AddressSize = address_size;
	info.IsBigEndian = is_bigendian != 0;
	return info;
}

PortableServer_ServantBase__epv base_epv = {
	NULL,
	NULL,
	NULL
};

POA_Debugger_Manager__epv debugger_manager_epv = {
	NULL,
	(gpointer) &do_spawn,
	(gpointer) &do_attach,
	(gpointer) &do_get_target_info
};
POA_Debugger_Manager__vepv poa_debugger_manager_vepv = { &base_epv, &debugger_manager_epv };
POA_Debugger_Manager poa_debugger_manager_servant = { NULL, &poa_debugger_manager_vepv };

static void
debugger_manager_srv_start_poa (CORBA_Environment *ev)
{
	const static int    MAX_POLICIES  = 1;
	CORBA_PolicyList   *poa_policies;

	poa_policies           = CORBA_PolicyList__alloc ();
        poa_policies->_maximum = MAX_POLICIES;
        poa_policies->_length  = MAX_POLICIES;
        poa_policies->_buffer  = CORBA_PolicyList_allocbuf (MAX_POLICIES);
        CORBA_sequence_set_release (poa_policies, CORBA_TRUE);
                                                                                
        poa_policies->_buffer[0] = (CORBA_Policy)
		PortableServer_POA_create_thread_policy (
			rootpoa,
			PortableServer_SINGLE_THREAD_MODEL,
			ev);

	the_poa = PortableServer_POA_create_POA (rootpoa,
						 "Debugger POA",
						 rootpoa_mgr,
						 poa_policies,
						 ev);
	g_assert (!ev->_major);

        CORBA_Policy_destroy (poa_policies->_buffer[0], ev); 
	g_assert (!ev->_major);
	CORBA_free (poa_policies);
}

static void
lock_bpm (void)
{ }

static void
unlock_bpm (void)
{ }

static void
debugger_manager_srv_start_object (CORBA_Environment *ev)
{
	bpm = mono_debugger_breakpoint_manager_new (lock_bpm, unlock_bpm);

	POA_Debugger_Manager__init (&poa_debugger_manager_servant, ev);
	g_assert (!ev->_major);

	the_objid = PortableServer_POA_activate_object (
		the_poa, &poa_debugger_manager_servant, ev);
	g_assert (!ev->_major);

	debugger_manager = PortableServer_POA_servant_to_reference (
		the_poa, &poa_debugger_manager_servant, ev);
	g_assert (!ev->_major);
}

static void
debugger_manager_srv_finish_object (CORBA_Environment *ev)
{
	CORBA_Object_release (debugger_manager, ev);
	g_assert (!ev->_major);

	debugger_manager = 0;
	PortableServer_POA_deactivate_object (the_poa, the_objid, ev);
	g_assert (!ev->_major);

	CORBA_free (the_objid);
	the_objid = 0;
	POA_Debugger_Manager__fini (&poa_debugger_manager_servant, ev);
	g_assert (!ev->_major);
}

int
mono_thread_get_abort_signal (void)
{
	return -1;
}

static gpointer
debugger_thread (gpointer data)
{
	CORBA_Environment ev;
	char *argv[6] = { "remoting-client", "--ORBIIOPIPv4=1", "--ORBIIOPUNIX=0",
			  "--ORBIIOPIPName=127.0.0.1", "--ORBIIOPIPSock=40860",
			  NULL };
	int argc = 5;

	argv [4] = g_strdup_printf ("--ORBIIOPIPSock=%d", the_port);

	CORBA_exception_init (&ev);
	orb = CORBA_ORB_init (&argc, argv, "orbit-local-mt-orb", &ev);
	g_assert (!ev._major);

        rootpoa = (PortableServer_POA) 
		CORBA_ORB_resolve_initial_references (orb, "RootPOA", &ev);
	g_assert (!ev._major);

        rootpoa_mgr = PortableServer_POA__get_the_POAManager (rootpoa, &ev);
	g_assert (!ev._major);

	PortableServer_POAManager_activate (rootpoa_mgr, &ev);

	debugger_manager_srv_start_poa (&ev);
	g_assert (!ev._major);

	debugger_manager_srv_start_object (&ev);
	the_ior = CORBA_ORB_object_to_string (orb, debugger_manager, &ev);
	g_assert (!ev._major);

	g_mutex_lock (ready_mutex);
	g_cond_signal (ready_cond);
	g_mutex_unlock (ready_mutex);

	CORBA_ORB_run (orb, &ev);

	debugger_manager_srv_finish_object (&ev);

	CORBA_Object_release ((CORBA_Object) rootpoa, &ev);
	g_assert (!ev._major);
	rootpoa = 0;

	CORBA_ORB_shutdown (orb, FALSE, &ev);
	g_assert (!ev._major);

	CORBA_exception_free (&ev);
	return NULL;
}

static gpointer
wait_thread (gpointer data)
{
	for (;;) {
		guint32 ret, status;

		g_cond_wait (cond, mutex);

		if (!global_vtable || !global_vtable->global_wait)
			continue;

		ret = global_vtable->global_wait (&status);

		ret = htonl (ret);
		status = htonl (status);

		g_assert (write (1, &ret, 4) == 4);
		g_assert (write (1, &status, 4) == 4);
	}
}

static void
write_string (const gchar *string)
{
	guint32 len = htonl (strlen (string));
	g_assert (write (1, &len, 4) == 4);
	g_assert (write (1, string, strlen (string)) == strlen (string));
}

int
main (int argc, char **argv)
{
	int magic;

	g_thread_init (NULL);

	magic = htonl (MONO_DEBUGGER_REMOTE_MAGIC);
	g_assert (write (1, &magic, 4) == 4);
	magic = htonl (MONO_DEBUGGER_REMOTE_VERSION);
	g_assert (write (1, &magic, 4) == 4);

	g_assert (read (0, &the_port, 4) == 4);
	the_port = ntohl (the_port);

	mutex = g_mutex_new ();
	cond = g_cond_new ();

	ready_mutex = g_mutex_new ();
	ready_cond = g_cond_new ();

	g_thread_create (debugger_thread, NULL, TRUE, NULL);
	g_thread_create (wait_thread, NULL, TRUE, NULL);

	g_cond_wait (ready_cond, ready_mutex);

	write_string (the_ior);

	for (;;) {
		guint32 command, len;

		len = read (0, &command, 4);
		if (len != 4) {
			g_warning (G_STRLOC ": Cannot recv: %s", g_strerror (errno));
			exit (1);
		}

		command = ntohl (command);

		switch (command) {
		case 1:
			g_mutex_lock (mutex);
			g_cond_signal (cond);
			g_mutex_unlock (mutex);
			break;

		case 2:
			global_vtable->global_stop ();
			break;

		default:
			g_assert_not_reached ();
		}
	}

	return 0;

}
