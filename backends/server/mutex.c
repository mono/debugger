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
