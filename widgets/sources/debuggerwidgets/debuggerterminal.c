#include "debuggerterminal.h"
#include <gdk/gdkkeysyms.h>
#include <gtk/gtkbindings.h>
#include <stdio.h>
#include <string.h>

static ZvtTermClass *parent_class = NULL;

static void
debugger_terminal_finalize (GObject *object)
{
	G_OBJECT_CLASS (parent_class)->finalize (object);
}

#if 0
static gint
debugger_terminal_key_press (GtkWidget *widget, GdkEventKey *event)
{
	DebuggerTerminal *terminal = DEBUGGER_TERMINAL (widget);

	if (gtk_bindings_activate (GTK_OBJECT (widget), event->keyval, event->state))
		return TRUE;

	if ((event->keyval < 0x20) || (event->keyval > 0xff))
		return GTK_WIDGET_CLASS (parent_class)->key_press_event (widget, event);

	g_message (G_STRLOC ": %d - %d", event->keyval, event->state);
	return TRUE;
}
#endif

static void
debugger_terminal_class_init (DebuggerTerminalClass *klass)
{
	GObjectClass *gobject_class = G_OBJECT_CLASS (klass);
	GtkWidgetClass *widget_class;

	widget_class = (GtkWidgetClass*) klass;
	parent_class = g_type_class_peek_parent (klass);

	gobject_class->finalize = debugger_terminal_finalize;
	// widget_class->key_press_event = debugger_terminal_key_press;
}

GType
debugger_terminal_get_type (void)
{
	static GType terminal_type = 0;

	if (!terminal_type) {
		static const GTypeInfo terminal_info = {
			sizeof (DebuggerTerminalClass),
			(GBaseInitFunc) NULL,
			(GBaseFinalizeFunc) NULL,
			(GClassInitFunc) debugger_terminal_class_init,
			NULL,	/* class_finalize */
			NULL,	/* class_data */
			sizeof (DebuggerTerminal),
			0,	/* n_preallocs */
			(GInstanceInitFunc) NULL
		};

		terminal_type = g_type_register_static (ZVT_TYPE_TERM,
							"DebuggerTerminal",
							&terminal_info, 0);
	}

	return terminal_type;
}

DebuggerTerminal*
debugger_terminal_new (void)
{
	return g_object_new (DEBUGGER_TYPE_TERMINAL, NULL);
}

void
debugger_terminal_feed (DebuggerTerminal *term, gchar *text)
{
	char buffer [BUFSIZ + 2], *ptr;
	int len, pos;

	len = strlen (text);
	for (ptr = text, pos = 0; *ptr; ptr++) {
		if (pos >= BUFSIZ) {
			buffer [pos] = 0;
			zvt_term_feed (ZVT_TERM (term), buffer, pos);
			pos = 0;
		}

		buffer [pos++] = *ptr;
		if (*ptr == '\n')
			buffer [pos++] = '\r';
	}

	if (pos) {
		buffer [pos] = 0;
		zvt_term_feed (ZVT_TERM (term), buffer, pos);
	}
}

void
debugger_terminal_set_scrollback (DebuggerTerminal *term, int scrollback)
{
	g_return_if_fail (term != NULL);
	g_return_if_fail (DEBUGGER_IS_TERMINAL (term));

	zvt_term_set_scrollback (ZVT_TERM (term), scrollback);
}

void
debugger_terminal_set_scroll_on_keystroke (DebuggerTerminal *term, gboolean state)
{
	g_return_if_fail (term != NULL);
	g_return_if_fail (DEBUGGER_IS_TERMINAL (term));

	zvt_term_set_scroll_on_keystroke (ZVT_TERM (term), state ? 1 : 0);
}

void
debugger_terminal_set_scroll_on_output (DebuggerTerminal *term, gboolean state)
{
	g_return_if_fail (term != NULL);
	g_return_if_fail (DEBUGGER_IS_TERMINAL (term));

	zvt_term_set_scroll_on_output (ZVT_TERM (term), state ? 1 : 0);
}

void
debugger_terminal_set_blink (DebuggerTerminal *term, gboolean state)
{
	g_return_if_fail (term != NULL);
	g_return_if_fail (DEBUGGER_IS_TERMINAL (term));

	zvt_term_set_blink (ZVT_TERM (term), state ? 1 : 0);
}

void
debugger_terminal_set_bell (DebuggerTerminal *term, gboolean state)
{
	g_return_if_fail (term != NULL);
	g_return_if_fail (DEBUGGER_IS_TERMINAL (term));

	zvt_term_set_bell (ZVT_TERM (term), state ? 1 : 0);
}

gboolean
debugger_terminal_get_bell (DebuggerTerminal *term)
{
	g_return_val_if_fail (term != NULL, FALSE);
	g_return_val_if_fail (DEBUGGER_IS_TERMINAL (term), FALSE);

	return zvt_term_get_bell (ZVT_TERM (term)) ? TRUE : FALSE;
}

void
debugger_terminal_set_font_name (DebuggerTerminal *term, const char *font_name)
{
	g_return_if_fail (term != NULL);
	g_return_if_fail (DEBUGGER_IS_TERMINAL (term));

	zvt_term_set_font_name (ZVT_TERM (term), font_name);
}

GtkAdjustment *
debugger_terminal_get_vadjustment (DebuggerTerminal *term)
{
	g_return_val_if_fail (term != NULL, NULL);
	g_return_val_if_fail (DEBUGGER_IS_TERMINAL (term), NULL);

	return GTK_ADJUSTMENT (ZVT_TERM (term)->adjustment);
}
