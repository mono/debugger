#include "debuggersourceview.h"
#include <gtk/gtksignal.h>
#include "debuggerwidgets-marshal.h"

enum {
	DEBUGGER_POPULATE_POPUP,
	LAST_SIGNAL
};

static guint signals [LAST_SIGNAL] = { 0 };

static GtkSourceViewClass *parent_class = NULL;

static void
debugger_source_view_finalize (GObject *object)
{
	DebuggerSourceView *source_view = DEBUGGER_SOURCE_VIEW (object);

	G_OBJECT_CLASS (parent_class)->finalize (object);
}

static void
debugger_source_view_populate_popup (GtkTextView *text_view, GtkMenu *menu)
{
	DebuggerSourceView *source_view;
	gint x, y;

	source_view = DEBUGGER_SOURCE_VIEW (text_view);

	GTK_TEXT_VIEW_CLASS (parent_class)->populate_popup (text_view, menu);

	g_signal_emit (G_OBJECT (text_view), signals [DEBUGGER_POPULATE_POPUP], 0,
		       menu, source_view->last_popup_x, source_view->last_popup_y);
}

static gint
debugger_source_view_button_press_event (GtkWidget *widget, GdkEventButton *event)
{
	DebuggerSourceView *source_view;

	source_view = DEBUGGER_SOURCE_VIEW (widget);

	if ((event->type == GDK_BUTTON_PRESS) && (event->button == 3)) {
		gint x, y, buffer_x, buffer_y, line_top, retval;
		GtkTextIter iter;

		g_message (G_STRLOC ": press %d,%d", (int) event->x, (int) event->y);

		source_view->last_popup_x = (int) event->x;
		source_view->last_popup_y = (int) event->y;
	}

	return GTK_WIDGET_CLASS (parent_class)->button_press_event (widget, event);
}

static void
debugger_source_view_class_init (DebuggerSourceViewClass *klass)
{
	GObjectClass *gobject_class = G_OBJECT_CLASS (klass);
	GtkTextViewClass *text_view_class;
	GtkWidgetClass *widget_class;
	GtkObjectClass *object_class;

	object_class = (GtkObjectClass*) klass;	
	widget_class = (GtkWidgetClass*) klass;
	text_view_class = (GtkTextViewClass*) klass;
	parent_class = g_type_class_peek_parent (klass);

	signals [DEBUGGER_POPULATE_POPUP] =
		gtk_signal_new ("debugger_populate_popup",
				GTK_RUN_LAST,
				GTK_CLASS_TYPE (object_class),
				GTK_SIGNAL_OFFSET (DebuggerSourceViewClass, debugger_populate_popup),
				debuggerwidgets_marshal_VOID__OBJECT_INT_INT,
				GTK_TYPE_NONE, 3, GTK_TYPE_MENU, GTK_TYPE_INT, GTK_TYPE_INT);

	gobject_class->finalize = debugger_source_view_finalize;
	widget_class->button_press_event = debugger_source_view_button_press_event;
	text_view_class->populate_popup = debugger_source_view_populate_popup;
}

GType
debugger_source_view_get_type (void)
{
	static GType source_view_type = 0;

	if (!source_view_type) {
		static const GTypeInfo source_view_info = {
			sizeof (DebuggerSourceViewClass),
			(GBaseInitFunc) NULL,
			(GBaseFinalizeFunc) NULL,
			(GClassInitFunc) debugger_source_view_class_init,
			NULL,	/* class_finalize */
			NULL,	/* class_data */
			sizeof (DebuggerSourceView),
			0,	/* n_preallocs */
			(GInstanceInitFunc) NULL
		};

		source_view_type = g_type_register_static (GTK_TYPE_SOURCE_VIEW,
							   "DebuggerSourceView",
							   &source_view_info, 0);
	}

	return source_view_type;
}

DebuggerSourceView *
debugger_source_view_new (void)
{
	return g_object_new (DEBUGGER_TYPE_SOURCE_VIEW, NULL);
}

DebuggerSourceView *
debugger_source_view_new_with_buffer (GtkSourceBuffer *buffer)
{
	DebuggerSourceView *view;

	view = debugger_source_view_new ();
	gtk_text_view_set_buffer (GTK_TEXT_VIEW (view), GTK_TEXT_BUFFER (buffer));

	return view;
}
