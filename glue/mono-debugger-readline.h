#ifndef __MONO_DEBUGGER_READLINE_H__
#define __MONO_DEBUGGER_READLINE_H__

#include <glib.h>
#include <stdio.h>
#include <readline/readline.h>

G_BEGIN_DECLS

extern void mono_debugger_readline_init (void);
extern char *mono_debugger_readline_readline (GIOChannel *channel, const char *prompt);
extern void mono_debugger_readline_add_history (const char *line);

G_END_DECLS

#endif
