#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <debugger-srv.h>
#include <orbit/poa/orbit-adaptor.h>
#include <pthread.h>

#if defined(__POWERPC__)
extern InferiorVTable powerpc_darwin_inferior;
InferiorVTable *global_vtable = &powerpc_darwin_inferior;
#else
extern InferiorVTable i386_ptrace_inferior;
InferiorVTable *global_vtable = &i386_ptrace_inferior;
#endif

typedef struct
{
	POA_Debugger_Thread servant;
	PortableServer_POA poa;
	ServerHandle *handle;
} impl_POA_Debugger_Thread;

static CORBA_long
do_dispatch_event (impl_POA_Debugger_Thread *servant, const CORBA_long_long status,
		   Debugger_Address *arg, Debugger_Address *data1, Debugger_Address *data2,
		   CORBA_Environment *ev)
{
	CHECK_VTABLE_RET (dispatch_event, -1);

	return global_vtable->dispatch_event (servant->handle, status, arg, data1, data2);
}

static Debugger_Address
do_get_frame (impl_POA_Debugger_Thread *servant, CORBA_Environment *ev)
{
	ServerCommandError result;
	guint64 pc;

	CHECK_VTABLE_RET (get_pc, -1);
	result = global_vtable->get_pc (servant->handle, &pc);
	CHECK_RESULT_RET (-1);
	return pc;
}

static CORBA_boolean
do_current_insn_is_bpt (impl_POA_Debugger_Thread *servant, CORBA_Environment *ev)
{
	ServerCommandError result;
	guint32 ret;

	CHECK_VTABLE_RET (current_insn_is_bpt, FALSE);
	result = global_vtable->current_insn_is_bpt (servant->handle, &ret);
	CHECK_RESULT_RET (FALSE);
	return ret != 0;
}

static void
do_step (impl_POA_Debugger_Thread *servant, CORBA_Environment *ev)
{
	ServerCommandError result;

	CHECK_VTABLE (step);
	result = global_vtable->step (servant->handle);
	CHECK_RESULT;
}

static void
do_continue (impl_POA_Debugger_Thread *servant, CORBA_Environment *ev)
{
	ServerCommandError result;

	CHECK_VTABLE (run);
	result = global_vtable->run (servant->handle);
	CHECK_RESULT;
}

static void
do_detach (impl_POA_Debugger_Thread *servant, CORBA_Environment *ev)
{
	ServerCommandError result;

	CHECK_VTABLE (detach);
	result = global_vtable->detach (servant->handle);
	CHECK_RESULT;
}

static CORBA_long
do_peek_word (impl_POA_Debugger_Thread *servant, const Debugger_Address addr,
	      CORBA_Environment *ev)
{
	ServerCommandError result;
	guint32 word;

	CHECK_VTABLE_RET (peek_word, 0);
	result = global_vtable->peek_word (servant->handle, addr, &word);
	CHECK_RESULT_RET (0);
	return word;
}

static Debugger_Blob *
do_read_memory (impl_POA_Debugger_Thread *servant, const Debugger_Address address,
		const CORBA_long size, CORBA_Environment *ev)
{
	ServerCommandError result;
	Debugger_Blob *blob;

	CHECK_VTABLE_RET (read_memory, NULL);
	blob = Debugger_Blob__alloc ();
	blob->_length = blob->_maximum = size;
	blob->_buffer = Debugger_Blob_allocbuf (size);
	result = global_vtable->read_memory (servant->handle, address, size, blob->_buffer);
	if (result != COMMAND_ERROR_NONE)
		CORBA_free (blob);
	CHECK_RESULT_RET (NULL);
	return blob;
}

static void
do_write_memory (impl_POA_Debugger_Thread *servant, const Debugger_Address address,
		 const Debugger_Blob *data, CORBA_Environment *ev)
{
	ServerCommandError result;

	CHECK_VTABLE (write_memory);
	result = global_vtable->write_memory (
		servant->handle, address, data->_length, data->_buffer);
	CHECK_RESULT;
}

static CORBA_long
do_insert_breakpoint (impl_POA_Debugger_Thread *servant, const Debugger_Address address,
		      CORBA_Environment *ev)
{
	ServerCommandError result;
	guint32 breakpoint;

	CHECK_VTABLE_RET (insert_breakpoint, -1);
	result = global_vtable->insert_breakpoint (servant->handle, address, &breakpoint);
	CHECK_RESULT_RET (-1);
	return breakpoint;
}

static CORBA_long
do_insert_hw_breakpoint (impl_POA_Debugger_Thread *servant, const CORBA_long index,
			 const Debugger_Address address, CORBA_Environment *ev)
{
	ServerCommandError result;
	guint32 breakpoint;

	CHECK_VTABLE_RET (insert_hw_breakpoint, -1);
	result = global_vtable->insert_hw_breakpoint (
		servant->handle, index, address, &breakpoint);
	CHECK_RESULT_RET (-1);
	return breakpoint;
}

static void
do_remove_breakpoint (impl_POA_Debugger_Thread *servant, const CORBA_long index,
		      CORBA_Environment * ev)
{
	ServerCommandError result;

	CHECK_VTABLE (remove_breakpoint);
	result = global_vtable->remove_breakpoint (servant->handle, index);
	CHECK_RESULT;
}

static void
do_enable_breakpoint (impl_POA_Debugger_Thread *servant, const CORBA_long index,
		      CORBA_Environment * ev)
{
	ServerCommandError result;

	CHECK_VTABLE (enable_breakpoint);
	result = global_vtable->enable_breakpoint (servant->handle, index);
	CHECK_RESULT;
}

static void
do_disable_breakpoint (impl_POA_Debugger_Thread *servant, const CORBA_long index,
		       CORBA_Environment * ev)
{
	ServerCommandError result;

	CHECK_VTABLE (disable_breakpoint);
	result = global_vtable->disable_breakpoint (servant->handle, index);
	CHECK_RESULT;
}

static void
do_get_registers (impl_POA_Debugger_Thread *servant, Debugger_RegisterList *list,
		  CORBA_Environment *ev)
{
	ServerCommandError result;
	guint32 *registers;
	guint64 *values;
	int i;

	CHECK_VTABLE (get_registers);
	registers = g_new0 (guint32, list->_length);
	values = g_new0 (guint64, list->_length);
	for (i = 0; i < list->_length; i++)
		registers [i] = list->_buffer [i].Index;

	result = global_vtable->get_registers (
		servant->handle, list->_length, registers, values);
	if (result != COMMAND_ERROR_NONE) {
		g_free (registers);
		g_free (values);
	}
	CHECK_RESULT;

	for (i = 0; i < list->_length; i++)
		list->_buffer [i].Value = values [i];

	g_free (registers);
	g_free (values);
}

static void
do_set_registers (impl_POA_Debugger_Thread *servant, Debugger_RegisterList *list,
		  CORBA_Environment *ev)
{
	ServerCommandError result;
	guint32 *registers;
	guint64 *values;
	int i;

	CHECK_VTABLE (set_registers);
	registers = g_new0 (guint32, list->_length);
	values = g_new0 (guint64, list->_length);
	for (i = 0; i < list->_length; i++) {
		registers [i] = list->_buffer [i].Index;
		values [i] = list->_buffer [i].Value;
	}

	result = global_vtable->set_registers (
		servant->handle, list->_length, registers, values);
	g_free (registers);
	g_free (values);
	CHECK_RESULT;
}

static Debugger_Address
do_get_return_addr (impl_POA_Debugger_Thread *servant, CORBA_Environment *ev)
{
	ServerCommandError result;
	guint64 addr;

	CHECK_VTABLE_RET (get_ret_address, -1);
	result = global_vtable->get_ret_address (servant->handle, &addr);
	CHECK_RESULT_RET (-1);
	return addr;
}

static Debugger_StackFrameList *
do_get_backtrace (impl_POA_Debugger_Thread *servant, const CORBA_long MaxFrames,
		  const Debugger_Address StopAddress, CORBA_Environment * ev)
{
	ServerCommandError result;
	Debugger_StackFrameList *list;
	StackFrame *frames = NULL;
	guint32 count, i;

	CHECK_VTABLE_RET (get_backtrace, NULL);
	result = global_vtable->get_backtrace (
		servant->handle, MaxFrames, StopAddress, &count, &frames);
	CHECK_RESULT_RET (NULL);

	list = Debugger_StackFrameList__alloc ();
	list->_length = list->_maximum = count;
	list->_buffer = Debugger_StackFrameList_allocbuf (count);

	for (i = 0; i < count; i++) {
		list->_buffer [i].Frame = frames [i].address;
		list->_buffer [i].FrameAddress = frames [i].frame_address;
	}

	g_free (frames);

	return list;	
}

static void
do_stop (impl_POA_Debugger_Thread *servant, CORBA_Environment *ev)
{
	ServerCommandError result;

	CHECK_VTABLE (stop);
	result = global_vtable->stop (servant->handle);
	CHECK_RESULT;
}

static CORBA_long_long
do_stop_and_wait (impl_POA_Debugger_Thread *servant, CORBA_Environment *ev)
{
	ServerCommandError result;
	CORBA_long status;

	CHECK_VTABLE_RET (stop_and_wait, -1);
	result = global_vtable->stop_and_wait (servant->handle, &status);
	CHECK_RESULT_RET (-1);
	return status;
}

static void
impl_Debugger_Thread__fini (impl_POA_Debugger_Thread *servant, CORBA_Environment *ev)
{
	CORBA_Object_release ((CORBA_Object) servant->poa, ev);

	global_vtable->finalize (servant->handle);

	POA_Debugger_Thread__fini ((PortableServer_Servant) servant, ev);
	g_free (servant);
}

PortableServer_ServantBase__epv debugger_thread_base_epv = {
	NULL,
	(gpointer) &impl_Debugger_Thread__fini,
	NULL
};

POA_Debugger_Thread__epv debugger_thread_epv = {
	NULL,
	(gpointer) &do_dispatch_event,
	(gpointer) &do_get_frame,
	(gpointer) &do_current_insn_is_bpt,
	(gpointer) &do_step,
	(gpointer) &do_continue,
	(gpointer) &do_detach,
	(gpointer) &do_peek_word,
	(gpointer) &do_read_memory,
	(gpointer) &do_write_memory,
	(gpointer) &do_insert_breakpoint,
	(gpointer) &do_insert_hw_breakpoint,
	(gpointer) &do_remove_breakpoint,
	(gpointer) &do_enable_breakpoint,
	(gpointer) &do_disable_breakpoint,
	(gpointer) &do_get_registers,
	(gpointer) &do_set_registers,
	(gpointer) &do_get_return_addr,
	(gpointer) &do_get_backtrace,
	(gpointer) &do_stop,
	(gpointer) &do_stop_and_wait
};

POA_Debugger_Thread__vepv poa_debugger_thread_vepv = {
	&debugger_thread_base_epv, &debugger_thread_epv
};

Debugger_Thread
debugger_srv_start_object (ServerHandle *handle, CORBA_Environment *ev)
{
	impl_POA_Debugger_Thread *newservant;
	PortableServer_ObjectId *objid;

	newservant = g_new0 (impl_POA_Debugger_Thread, 1);
	newservant->servant.vepv = &poa_debugger_thread_vepv;
	newservant->poa = (PortableServer_POA) CORBA_Object_duplicate (
		(CORBA_Object) the_poa, ev);
	POA_Debugger_Thread__init ((PortableServer_Servant) newservant, ev);

	newservant->handle = handle;

	objid = PortableServer_POA_activate_object (the_poa, newservant, ev);
	CORBA_free (objid);

	return PortableServer_POA_servant_to_reference (the_poa, newservant, ev);
}

