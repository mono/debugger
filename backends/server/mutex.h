#ifndef __MONO_DEBUGGER_MUTEX_H__
#define __MONO_DEBUGGER_MUTEX_H__

#include <glib.h>
#include <glib/gthread.h>

G_BEGIN_DECLS

typedef struct {
	GCond *cond;
	GMutex *mutex;
	gboolean signaled;
	gboolean waiting;
} DebuggerEvent;

GMutex *
mono_debugger_mutex_new          (void);

void
mono_debugger_mutex_lock         (GMutex *mutex);

void
mono_debugger_mutex_unlock       (GMutex *mutex);

gboolean
mono_debugger_mutex_trylock      (GMutex *mutex);

DebuggerEvent *
mono_debugger_event_new          (void);

void
mono_debugger_event_wait         (DebuggerEvent *event);

gboolean
mono_debugger_event_trywait      (DebuggerEvent *event);

void
mono_debugger_event_signal       (DebuggerEvent *event);

G_END_DECLS

#endif
