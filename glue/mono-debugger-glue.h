#ifndef __MONO_DEBUGGER_GLUE_H__
#define __MONO_DEBUGGER_GLUE_H__

#include <glib.h>

G_BEGIN_DECLS

typedef void (*IOStringInputHandler) (const char *input);
typedef void (*IODataInputHandler) (int byte);
typedef void (*IOHangupHandler) (void);

extern unsigned mono_debugger_io_add_watch_string_input (GIOChannel *channel, IOStringInputHandler cb);
extern unsigned mono_debugger_io_add_watch_data_input (GIOChannel *channel, IODataInputHandler cb);
extern unsigned mono_debugger_io_add_watch_hangup (GIOChannel *channel, IOHangupHandler cb);
extern void mono_debugger_io_set_async (GIOChannel *channel, gboolean is_async);
extern int mono_debugger_io_read_byte (GIOChannel *channel);
extern int mono_debugger_io_write_byte (GIOChannel *channel, int data);
extern void mono_debugger_io_write_line (GIOChannel *channel, const char *line);
extern void mono_debugger_io_set_data_mode (GIOChannel *channel);

extern void mono_debugger_glue_kill_process (int pid, int force);
extern void mono_debugger_glue_make_pipe (guint32 *input, guint32 *output);
extern void mono_debugger_glue_close_pipe (guint32 input, guint32 output);

G_END_DECLS

#endif
