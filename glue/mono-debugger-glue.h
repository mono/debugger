#ifndef __MONO_DEBUGGER_GLUE_H__
#define __MONO_DEBUGGER_GLUE_H__

#include <glib.h>

G_BEGIN_DECLS

typedef void (*MonoDebuggerGlueReadHandler) (const char *input);
typedef void (*MonoDebuggerGlueHangupHandler) (void);
typedef void (*MonoDebuggerGlueEventHandler) (void);

extern unsigned mono_debugger_glue_add_watch_input (GIOChannel *channel, MonoDebuggerGlueReadHandler cb);
extern unsigned mono_debugger_glue_add_watch_hangup (GIOChannel *channel, MonoDebuggerGlueHangupHandler cb);
extern GSource *mono_debugger_glue_create_watch_input (GIOChannel *channel, MonoDebuggerGlueEventHandler cb);
extern int mono_debugger_glue_read_byte (GIOChannel *channel);
extern int mono_debugger_glue_write_byte (GIOChannel *channel, int data);
extern void mono_debugger_glue_add_watch_output (GIOChannel *channel);
extern void mono_debugger_glue_setup_data_output (GIOChannel *channel);
extern void mono_debugger_glue_kill_process (int pid, int force);
extern void mono_debugger_glue_write_line (GIOChannel *channel, const char *line);
extern void mono_debugger_glue_make_pipe (guint32 *input, guint32 *output);
extern void mono_debugger_glue_close_pipe (guint32 input, guint32 output);

G_END_DECLS

#endif
