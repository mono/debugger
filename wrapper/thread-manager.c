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
volatile int MONO_DEBUGGER__thread_manager = 0;
volatile int MONO_DEBUGGER__thread_manager_last_pid = 0;
volatile gpointer MONO_DEBUGGER__thread_manager_last_func = NULL;
volatile MonoThread *MONO_DEBUGGER__thread_manager_last_thread = NULL;
static MonoThread *last_thread = NULL;

/*
 * NOTE: We must not call any functions here which we ever may want to debug !
 */
static guint32
thread_manager_func (gpointer dummy)
{
	int last_stack = 0;

	mono_new_thread_init (NULL, &last_stack, NULL);
	MONO_DEBUGGER__thread_manager = getpid ();

	/*
	 * The parent thread waits on this condition because it needs our pid.
	 */
	IO_LAYER (ReleaseSemaphore) (thread_manager_start_cond, 1, NULL);

	/*
	 * This mutex is locked by the parent thread until the debugger actually
	 * attached to us, so we don't need a SIGSTOP here anymore.
	 */
	IO_LAYER (EnterCriticalSection) (&thread_manager_mutex);

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

	return 0;
}

volatile void
MONO_DEBUGGER__main (void)
{ }

void
mono_debugger_thread_manager_init (void)
{
	HANDLE thread;

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

	thread = IO_LAYER (CreateThread) (NULL, 0, thread_manager_func, NULL, FALSE, NULL);
	g_assert (thread);

	/*
	 * Wait until the background thread set its pid.
	 */
	mono_debugger_wait_cond (thread_manager_start_cond);

	MONO_DEBUGGER__main ();

	IO_LAYER (LeaveCriticalSection) (&thread_manager_mutex);
}

static void
signal_thread_manager (MonoThread *thread, int pid, gpointer func)
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
	MONO_DEBUGGER__thread_manager_last_thread = NULL;
}

void
mono_debugger_thread_manager_add_thread (MonoThread *thread, int pid, gpointer func)
{
	IO_LAYER (EnterCriticalSection) (&thread_manager_finished_mutex);
	g_assert (!last_thread || (last_thread == thread));

	signal_thread_manager (last_thread, pid, func);

	if (last_thread)
		IO_LAYER (ReleaseSemaphore) (thread_manager_thread_started_cond, 1, NULL);

	IO_LAYER (LeaveCriticalSection) (&thread_manager_finished_mutex);
}

void
mono_debugger_thread_manager_start_resume (MonoThread *thread)
{
	IO_LAYER (EnterCriticalSection) (&thread_manager_start_mutex);

	IO_LAYER (EnterCriticalSection) (&thread_manager_finished_mutex);
	g_assert (last_thread == NULL);
	last_thread = thread;
	IO_LAYER (LeaveCriticalSection) (&thread_manager_finished_mutex);
}

void
mono_debugger_thread_manager_end_resume (MonoThread *thread)
{
	mono_debugger_wait_cond (thread_manager_thread_started_cond);

	IO_LAYER (EnterCriticalSection) (&thread_manager_finished_mutex);
	g_assert (last_thread == thread);
	signal_thread_manager (last_thread, -1, NULL);
	IO_LAYER (LeaveCriticalSection) (&thread_manager_finished_mutex);

	IO_LAYER (LeaveCriticalSection) (&thread_manager_start_mutex);
}
