#include <server.h>
#include <Debugger.h>

G_BEGIN_DECLS

extern CORBA_ORB orb;
extern PortableServer_POA rootpoa;
extern PortableServer_POA the_poa;
extern Debugger_Manager debugger_manager;

extern InferiorVTable *global_vtable;

#define CHECK_VTABLE(name) \
	if (!global_vtable->name) { \
		CORBA_exception_set_system (ev, ex_CORBA_NO_IMPLEMENT, CORBA_COMPLETED_NO); \
		return;	\
	}

#define CHECK_RESULT \
	if (result != COMMAND_ERROR_NONE) {				\
		Debugger_Error *ex = Debugger_Error__alloc ();		\
		ex->condition = result;					\
		CORBA_exception_set (ev, CORBA_USER_EXCEPTION, ex_Debugger_Error, ex); \
		return;							\
	}

#define CHECK_VTABLE_RET(name,ret) \
	if (!global_vtable->name) { \
		CORBA_exception_set_system (ev, ex_CORBA_NO_IMPLEMENT, CORBA_COMPLETED_NO); \
		return ret; \
	}

#define CHECK_RESULT_RET(ret) \
	if (result != COMMAND_ERROR_NONE) {				\
		Debugger_Error *ex = Debugger_Error__alloc ();		\
		ex->condition = result;					\
		CORBA_exception_set (ev, CORBA_USER_EXCEPTION, ex_Debugger_Error, ex); \
		return ret;						\
	}

Debugger_Thread
debugger_srv_start_object (ServerHandle *handle, CORBA_Environment *ev);

void
debugger_srv_finish_object (CORBA_Environment *ev);

void
debugger_srv_finish_poa (CORBA_Environment *ev);

G_END_DECLS
