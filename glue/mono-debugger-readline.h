#ifndef __MONO_DEBUGGER_READLINE_H__
#define __MONO_DEBUGGER_READLINE_H__

#include <glib.h>
#include <stdio.h>
#if USE_READLINE
#include <readline/readline.h>
#include <readline/history.h>
#endif

G_BEGIN_DECLS

extern int mono_debugger_readline_static_init (void);
extern int mono_debugger_readline_is_a_tty (int fd);
extern char *mono_debugger_readline_readline (const char *prompt);
extern void mono_debugger_readline_add_history (const char *line);

G_END_DECLS

#endif
