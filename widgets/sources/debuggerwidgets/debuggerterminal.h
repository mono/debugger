#ifndef __DEBUGGER_TERMINAL_H__
#define __DEBUGGER_TERMINAL_H__

#define HAS_I18N
#include <libzvt/libzvt.h>

G_BEGIN_DECLS

#define DEBUGGER_TYPE_TERMINAL             (debugger_terminal_get_type ())
#define DEBUGGER_TERMINAL(obj)             (GTK_CHECK_CAST ((obj), DEBUGGER_TYPE_TERMINAL, DebuggerTerminal))
#define DEBUGGER_TERMINAL_CLASS(klass)     (GTK_CHECK_CLASS_CAST ((klass), DEBUGGER_TYPE_TERMINAL, DebuggerTerminalClass))
#define DEBUGGER_IS_TERMINAL(obj)          (GTK_CHECK_TYPE ((obj), DEBUGGER_TYPE_TERMINAL))
#define DEBUGGER_IS_TERMINAL_CLASS(klass)  (GTK_CHECK_CLASS_TYPE ((klass), DEBUGGER_TYPE_TERMINAL))

typedef struct _DebuggerTerminal DebuggerTerminal;
typedef struct _DebuggerTerminalClass DebuggerTerminalClass;

struct _DebuggerTerminal
{
        ZvtTerm zvt;
};

struct _DebuggerTerminalClass
{
        ZvtTermClass parent_class;
};

GType             debugger_terminal_get_type                (void);
DebuggerTerminal *debugger_terminal_new                     (void);
void              debugger_terminal_feed                    (DebuggerTerminal *term, gchar *text);
void              debugger_terminal_set_scrollback          (DebuggerTerminal *term, int scrollback);
void              debugger_terminal_set_scroll_on_keystroke (DebuggerTerminal *term, gboolean state);
void              debugger_terminal_set_scroll_on_output    (DebuggerTerminal *term, gboolean state);
void              debugger_terminal_set_blink               (DebuggerTerminal *term, gboolean state);
void              debugger_terminal_set_bell                (DebuggerTerminal *term, gboolean state);
gboolean          debugger_terminal_get_bell                (DebuggerTerminal *term);
void              debugger_terminal_set_font_name           (DebuggerTerminal *term, const char *font_name);
GtkAdjustment    *debugger_terminal_get_vadjustment         (DebuggerTerminal *term);

G_END_DECLS

#endif /* __DEBUGGER_TERMINAL_H__ */
