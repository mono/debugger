#include <mono-debugger-jit-wrapper.h>
#include <mono/io-layer/io-layer.h>
#include <mono/metadata/threads.h>
#define IN_MONO_DEBUGGER
#include <mono/private/libgc-mono-debugger.h>
#include <unistd.h>
#include <string.h>

static GPtrArray *thread_array = NULL;

extern void GC_push_all_stack (gpointer b, gpointer t);

static void
debugger_gc_stop_world (void)
{
	mono_debugger_thread_manager_acquire_global_thread_lock ();
}

static void
debugger_gc_start_world (void)
{
	mono_debugger_thread_manager_release_global_thread_lock ();
}

static void
debugger_gc_push_all_stacks (void)
{
	int i, tid;

	tid = IO_LAYER (GetCurrentThreadId) ();

	if (!thread_array)
		return;

	for (i = 0; i < thread_array->len; i++) {
		MonoDebuggerThread *thread = g_ptr_array_index (thread_array, i);
		gpointer end_stack = (thread->tid == tid) ? &i : thread->end_stack;

		GC_push_all_stack (end_stack, thread->start_stack);
	}
}

GCThreadFunctions mono_debugger_thread_vtable = {
	NULL,

	debugger_gc_stop_world,
	debugger_gc_push_all_stacks,
	debugger_gc_start_world
};

void
mono_debugger_thread_manager_init (void)
{
	if (!thread_array)
		thread_array = g_ptr_array_new ();

	gc_thread_vtable = &mono_debugger_thread_vtable;
}

void
mono_debugger_thread_manager_add_thread (guint32 tid, gpointer start_stack, gpointer func)
{
	MonoDebuggerThread *thread = g_new0 (MonoDebuggerThread, 1);

	thread->tid = tid;
	thread->func = func;
	thread->start_stack = start_stack;

	mono_debugger_notification_function (NOTIFICATION_THREAD_CREATED, thread, tid);

	mono_debugger_thread_manager_thread_created (thread);
}

void
mono_debugger_thread_manager_thread_created (MonoDebuggerThread *thread)
{
	if (!thread_array)
		thread_array = g_ptr_array_new ();

	g_ptr_array_add (thread_array, thread);
}

void
mono_debugger_thread_manager_start_resume (guint32 tid)
{
}

void
mono_debugger_thread_manager_end_resume (guint32 tid)
{
}

void
mono_debugger_thread_manager_acquire_global_thread_lock (void)
{
	int tid = IO_LAYER (GetCurrentThreadId) ();

	mono_debugger_notification_function (
		NOTIFICATION_ACQUIRE_GLOBAL_THREAD_LOCK, NULL, tid);
}

void
mono_debugger_thread_manager_release_global_thread_lock (void)
{
	int tid = IO_LAYER (GetCurrentThreadId) ();

	mono_debugger_notification_function (
		NOTIFICATION_RELEASE_GLOBAL_THREAD_LOCK, NULL, tid);
}
