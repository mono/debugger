#ifndef __MONO_DEBUGGER_GLUE_H__
#define __MONO_DEBUGGER_GLUE_H__

#include <glib.h>

G_BEGIN_DECLS

typedef void (*MonoDebuggerGlueReadHandler) (const char *input);

extern unsigned mono_debugger_glue_add_watch_input (GIOChannel *channel, MonoDebuggerGlueReadHandler cb);
extern void mono_debugger_glue_add_watch_output (GIOChannel *channel);
extern void mono_debugger_glue_kill_process (int pid, int force);
extern void mono_debugger_glue_write_line (GIOChannel *channel, const char *line);

G_END_DECLS

#endif
