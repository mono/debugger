#include <mono-debugger-jit-wrapper.h>
#include <mono/io-layer/io-layer.h>
#include <mono/metadata/threads.h>
#define IN_MONO_DEBUGGER
#include <mono/private/libgc-mono-debugger.h>
#include <unistd.h>
#include <string.h>

static gpointer thread_manager_start_cond;
static gpointer thread_manager_cond;
static gpointer thread_manager_finished_cond;
static gpointer thread_manager_thread_started_cond;
static CRITICAL_SECTION thread_manager_start_mutex;
static CRITICAL_SECTION thread_manager_finished_mutex;
static CRITICAL_SECTION thread_manager_mutex;
static GPtrArray *thread_array = NULL;

static void (*notification_function) (int tid, gpointer data);

volatile gpointer MONO_DEBUGGER__thread_manager_notification = NULL;
volatile int MONO_DEBUGGER__thread_manager_notify_command = 0;
volatile int MONO_DEBUGGER__thread_manager_notify_tid = 0;
volatile gpointer MONO_DEBUGGER__thread_manager_notify_data = NULL;
static int last_tid = 0, last_pid = 0;

void
mono_debugger_thread_manager_main (void)
{
	notification_function (0, NULL);

	while (TRUE) {
		/* Wait for an event. */
		IO_LAYER (LeaveCriticalSection) (&thread_manager_mutex);
		mono_debugger_wait_cond (thread_manager_cond);
		IO_LAYER (EnterCriticalSection) (&thread_manager_mutex);

		/*
		 * Send notification - we'll stop on a breakpoint instruction at a special
		 * address.  The debugger will reload the thread list while we're stopped -
		 * and owning the `thread_manager_mutex' so that no other thread can touch
		 * them in the meantime.
		 */
		notification_function (0, NULL);

		IO_LAYER (ReleaseSemaphore) (thread_manager_finished_cond, 1, NULL);
	}
}

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
	int i, pid;

	pid = getpid ();

	if (!thread_array)
		return;

	for (i = 0; i < thread_array->len; i++) {
		MonoDebuggerThread *thread = g_ptr_array_index (thread_array, i);
		gpointer end_stack = (thread->pid == pid) ? &i : thread->end_stack;

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
	IO_LAYER (InitializeCriticalSection) (&thread_manager_mutex);
	IO_LAYER (InitializeCriticalSection) (&thread_manager_finished_mutex);
	IO_LAYER (InitializeCriticalSection) (&thread_manager_start_mutex);
	thread_manager_cond = IO_LAYER (CreateSemaphore) (NULL, 0, 1, NULL);
	thread_manager_start_cond = IO_LAYER (CreateSemaphore) (NULL, 0, 1, NULL);
	thread_manager_finished_cond = IO_LAYER (CreateSemaphore) (NULL, 0, 1, NULL);
	thread_manager_thread_started_cond = IO_LAYER (CreateSemaphore) (NULL, 0, 1, NULL);

	if (!thread_array)
		thread_array = g_ptr_array_new ();

	gc_thread_vtable = &mono_debugger_thread_vtable;

	notification_function = mono_debugger_create_notification_function (
		(gpointer) &MONO_DEBUGGER__thread_manager_notification);

	IO_LAYER (EnterCriticalSection) (&thread_manager_mutex);

	notification_function (0, NULL);
}

static void
signal_thread_manager (guint32 command, guint32 tid, gpointer data)
{
	IO_LAYER (EnterCriticalSection) (&thread_manager_mutex);
	MONO_DEBUGGER__thread_manager_notify_command = command;
	MONO_DEBUGGER__thread_manager_notify_tid = tid;
	MONO_DEBUGGER__thread_manager_notify_data = data;
	IO_LAYER (ReleaseSemaphore) (thread_manager_cond, 1, NULL);
	IO_LAYER (LeaveCriticalSection) (&thread_manager_mutex);

	mono_debugger_wait_cond (thread_manager_finished_cond);
	MONO_DEBUGGER__thread_manager_notify_command = 0;
	MONO_DEBUGGER__thread_manager_notify_tid = 0;
	MONO_DEBUGGER__thread_manager_notify_data = NULL;
}

void
mono_debugger_thread_manager_add_thread (guint32 tid, gpointer start_stack, gpointer func)
{
	MonoDebuggerThread *thread = g_new0 (MonoDebuggerThread, 1);

	thread->tid = tid;
	thread->pid = getpid ();
	thread->func = func;
	thread->start_stack = start_stack;

	IO_LAYER (EnterCriticalSection) (&thread_manager_finished_mutex);
	g_assert (last_tid == tid);
	last_pid = thread->pid;

	signal_thread_manager (THREAD_MANAGER_CREATE_THREAD, thread->pid, thread);

	IO_LAYER (ReleaseSemaphore) (thread_manager_thread_started_cond, 1, NULL);

	mono_debugger_thread_manager_thread_created (thread);

	IO_LAYER (LeaveCriticalSection) (&thread_manager_finished_mutex);
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
	IO_LAYER (EnterCriticalSection) (&thread_manager_start_mutex);

	IO_LAYER (EnterCriticalSection) (&thread_manager_finished_mutex);
	g_assert (last_tid == 0);
	last_tid = tid;
	IO_LAYER (LeaveCriticalSection) (&thread_manager_finished_mutex);
}

void
mono_debugger_thread_manager_end_resume (guint32 tid)
{
	mono_debugger_wait_cond (thread_manager_thread_started_cond);

	IO_LAYER (EnterCriticalSection) (&thread_manager_finished_mutex);
	g_assert (last_tid == tid);
	signal_thread_manager (THREAD_MANAGER_RESUME_THREAD, last_pid, NULL);
	last_tid = last_pid = 0;
	IO_LAYER (LeaveCriticalSection) (&thread_manager_finished_mutex);

	IO_LAYER (LeaveCriticalSection) (&thread_manager_start_mutex);
}

void
mono_debugger_thread_manager_acquire_global_thread_lock (void)
{
	IO_LAYER (EnterCriticalSection) (&thread_manager_finished_mutex);

	signal_thread_manager (THREAD_MANAGER_ACQUIRE_GLOBAL_LOCK, getpid (), NULL);

	IO_LAYER (LeaveCriticalSection) (&thread_manager_finished_mutex);
}

void
mono_debugger_thread_manager_release_global_thread_lock (void)
{
	IO_LAYER (EnterCriticalSection) (&thread_manager_finished_mutex);

	signal_thread_manager (THREAD_MANAGER_RELEASE_GLOBAL_LOCK, getpid (), NULL);

	IO_LAYER (LeaveCriticalSection) (&thread_manager_finished_mutex);
}
