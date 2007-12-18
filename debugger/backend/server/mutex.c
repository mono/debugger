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

GCond *
mono_debugger_cond_new (void)
{
	return g_cond_new ();
}

void
mono_debugger_cond_wait (GMutex *mutex, GCond *cond)
{
	return g_cond_wait (cond, mutex);
}

void
mono_debugger_cond_broadcast (GCond *cond)
{
	return g_cond_broadcast (cond);
}
