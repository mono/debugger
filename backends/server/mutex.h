#ifndef __MONO_DEBUGGER_MUTEX_H__
#define __MONO_DEBUGGER_MUTEX_H__

#include <glib.h>
#include <glib/gthread.h>

G_BEGIN_DECLS

GMutex *
mono_debugger_mutex_new          (void);

void
mono_debugger_mutex_lock         (GMutex *mutex);

void
mono_debugger_mutex_unlock       (GMutex *mutex);

gboolean
mono_debugger_mutex_trylock      (GMutex *mutex);

G_END_DECLS

#endif
