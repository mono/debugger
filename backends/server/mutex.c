#include <server.h>
#include <mutex.h>

GMutex *
mono_debugger_mutex_new (void)
{
	return g_mutex_new ();
}

void
mono_debugger_mutex_lock (GMutex *mutex)
{
	g_mutex_lock (mutex);
}

void
mono_debugger_mutex_unlock (GMutex *mutex)
{
	g_mutex_unlock (mutex);
}

gboolean
mono_debugger_mutex_trylock (GMutex *mutex)
{
	return g_mutex_trylock (mutex);
}

DebuggerEvent *
mono_debugger_event_new (void)
{
	DebuggerEvent *event = g_new0 (DebuggerEvent, 1);

	event->cond = g_cond_new ();
	event->mutex = g_mutex_new ();

	return event;
}

void
mono_debugger_event_wait (DebuggerEvent *event)
{
	g_mutex_lock (event->mutex);
	if (event->signaled) {
		event->signaled = FALSE;
		g_mutex_unlock (event->mutex);
		return;
	}
	event->waiting = TRUE;
	g_cond_wait (event->cond, event->mutex);
	event->waiting = FALSE;
	event->signaled = FALSE;
	g_mutex_unlock (event->mutex);
}

gboolean
mono_debugger_event_trywait (DebuggerEvent *event)
{
	gboolean retval;

	g_mutex_lock (event->mutex);
	retval = event->signaled;
	event->signaled = FALSE;
	g_mutex_unlock (event->mutex);

	return retval;
}

void
mono_debugger_event_signal (DebuggerEvent *event)
{
	g_mutex_lock (event->mutex);
	event->signaled = TRUE;
	if (event->waiting)
		g_cond_signal (event->cond);
	g_mutex_unlock (event->mutex);
}
