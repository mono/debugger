#include <server.h>
#include <mutex.h>

GMutex *
mono_debugger_mutex_new (void)
{
	return g_mutex_new ();
}

void
mono_debugger_mutex_free (GMutex *mutex)
{
	g_mutex_free (mutex);
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

GCond *
mono_debugger_cond_new (void)
{
	return g_cond_new ();
}

void
mono_debugger_cond_free (GCond *cond)
{
	g_cond_free (cond);
}

void
mono_debugger_cond_wait (GMutex *mutex, GCond *cond)
{
	g_cond_wait (cond, mutex);
}

gboolean
mono_debugger_cond_timed_wait (GMutex *mutex, GCond *cond, int milliseconds)
{
	GTimeVal time_val;
	g_get_current_time (&time_val);
	g_time_val_add (&time_val, milliseconds);
	return g_cond_timed_wait (cond, mutex, &time_val);
}

void
mono_debugger_cond_broadcast (GCond *cond)
{
	g_cond_broadcast (cond);
}
