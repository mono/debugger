#include "debuggerentry.h"
#include <gdk/gdkkeysyms.h>
#include <gtk/gtkbindings.h>
#include <gtk/gtksignal.h>
#include "debuggerwidgets-marshal.h"

enum {
	PREVIOUS_LINE,
	NEXT_LINE,
	LAST_SIGNAL
};

static guint signals[LAST_SIGNAL] = { 0 };

static GtkEntryClass *parent_class = NULL;

static void
debugger_entry_finalize (GObject *object)
{
	DebuggerEntry *entry = DEBUGGER_ENTRY (object);

	G_OBJECT_CLASS (parent_class)->finalize (object);
}

static gint
debugger_entry_key_press (GtkWidget *widget, GdkEventKey *event)
{
	DebuggerEntry *entry = DEBUGGER_ENTRY (widget);

	switch (event->keyval) {
	case GDK_Up:
		gtk_signal_emit (GTK_OBJECT (entry), signals[PREVIOUS_LINE]);
		return TRUE;

	case GDK_Down:
		gtk_signal_emit (GTK_OBJECT (entry), signals[NEXT_LINE]);
		return TRUE;

	default:
		return GTK_WIDGET_CLASS (parent_class)->key_press_event (widget, event);
	}
}

static void
debugger_entry_class_init (DebuggerEntryClass *klass)
{
	GObjectClass *gobject_class = G_OBJECT_CLASS (klass);
	GtkObjectClass *object_class;
	GtkWidgetClass *widget_class;
	GtkBindingSet *binding_set;

	object_class = (GtkObjectClass*) klass;
	widget_class = (GtkWidgetClass*) klass;
	parent_class = g_type_class_peek_parent (klass);

	gobject_class->finalize = debugger_entry_finalize;
	widget_class->key_press_event = debugger_entry_key_press;

	/*
	 * Signals
	 */

	signals[PREVIOUS_LINE] = 
		gtk_signal_new ("previous_line",
				GTK_RUN_LAST | GTK_RUN_ACTION,
				GTK_CLASS_TYPE (object_class),
				GTK_SIGNAL_OFFSET (DebuggerEntryClass, previous_line),
				gtk_marshal_VOID__VOID,
				GTK_TYPE_NONE, 0);
	signals[NEXT_LINE] = 
		gtk_signal_new ("next_line",
				GTK_RUN_LAST | GTK_RUN_ACTION,
				GTK_CLASS_TYPE (object_class),
				GTK_SIGNAL_OFFSET (DebuggerEntryClass, next_line),
				gtk_marshal_VOID__VOID,
				GTK_TYPE_NONE, 0);
}

GType
debugger_entry_get_type (void)
{
	static GType entry_type = 0;

	if (!entry_type) {
		static const GTypeInfo entry_info = {
			sizeof (DebuggerEntryClass),
			(GBaseInitFunc) NULL,
			(GBaseFinalizeFunc) NULL,
			(GClassInitFunc) debugger_entry_class_init,
			NULL,	/* class_finalize */
			NULL,	/* class_data */
			sizeof (DebuggerEntry),
			0,	/* n_preallocs */
			(GInstanceInitFunc) NULL
		};

		entry_type = g_type_register_static (GTK_TYPE_ENTRY,
						     "DebuggerEntry",
						     &entry_info, 0);
	}

	return entry_type;
}

DebuggerEntry*
debugger_entry_new (void)
{
	return g_object_new (DEBUGGER_TYPE_ENTRY, NULL);
}
