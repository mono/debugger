#include <mono-debugger-jit-wrapper.h>
#include <mono/jit/debug.h>
#include <mono/io-layer/io-layer.h>
#include <mono/metadata/threads.h>
#include <unistd.h>
#include <string.h>

static gpointer thread_manager_start_cond;
static gpointer thread_manager_cond;
static gpointer thread_manager_finished_cond;
static gpointer thread_manager_thread_started_cond;
static CRITICAL_SECTION thread_manager_start_mutex;
static CRITICAL_SECTION thread_manager_finished_mutex;
static CRITICAL_SECTION thread_manager_mutex;

static void (*notification_function) (int pid, gpointer func);

volatile gpointer MONO_DEBUGGER__thread_manager_notification = NULL;
volatile int MONO_DEBUGGER__thread_manager_last_pid = 0;
volatile gpointer MONO_DEBUGGER__thread_manager_last_func = NULL;
volatile guint32 MONO_DEBUGGER__thread_manager_last_thread = 0;
static guint32 last_thread = 0;

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

	notification_function = mono_debug_create_notification_function (
		(gpointer) &MONO_DEBUGGER__thread_manager_notification);

	IO_LAYER (EnterCriticalSection) (&thread_manager_mutex);

	notification_function (0, NULL);
}

static void
signal_thread_manager (guint32 thread, int pid, gpointer func)
{
	IO_LAYER (EnterCriticalSection) (&thread_manager_mutex);
	MONO_DEBUGGER__thread_manager_last_pid = pid;
	MONO_DEBUGGER__thread_manager_last_func = func;
	MONO_DEBUGGER__thread_manager_last_thread = thread;
	IO_LAYER (ReleaseSemaphore) (thread_manager_cond, 1, NULL);
	IO_LAYER (LeaveCriticalSection) (&thread_manager_mutex);

	mono_debugger_wait_cond (thread_manager_finished_cond);
	MONO_DEBUGGER__thread_manager_last_pid = 0;
	MONO_DEBUGGER__thread_manager_last_func = NULL;
	MONO_DEBUGGER__thread_manager_last_thread = 0;
}

void
mono_debugger_thread_manager_add_thread (guint32 thread, int pid, gpointer func)
{
	g_message (G_STRLOC ": %d - %d - %p", thread, pid, func);

	IO_LAYER (EnterCriticalSection) (&thread_manager_finished_mutex);
	g_assert (!last_thread || (last_thread == thread));

	signal_thread_manager (last_thread, pid, func);

	if (last_thread)
		IO_LAYER (ReleaseSemaphore) (thread_manager_thread_started_cond, 1, NULL);

	IO_LAYER (LeaveCriticalSection) (&thread_manager_finished_mutex);

	g_message (G_STRLOC ": %d", thread);
}

void
mono_debugger_thread_manager_start_resume (guint32 thread)
{
	IO_LAYER (EnterCriticalSection) (&thread_manager_start_mutex);

	IO_LAYER (EnterCriticalSection) (&thread_manager_finished_mutex);
	g_assert (last_thread == 0);
	last_thread = thread;
	IO_LAYER (LeaveCriticalSection) (&thread_manager_finished_mutex);
}

void
mono_debugger_thread_manager_end_resume (guint32 thread)
{
	mono_debugger_wait_cond (thread_manager_thread_started_cond);

	IO_LAYER (EnterCriticalSection) (&thread_manager_finished_mutex);
	g_assert (last_thread == thread);
	signal_thread_manager (last_thread, -1, NULL);
	IO_LAYER (LeaveCriticalSection) (&thread_manager_finished_mutex);

	IO_LAYER (LeaveCriticalSection) (&thread_manager_start_mutex);
}
