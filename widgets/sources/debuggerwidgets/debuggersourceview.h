#ifndef __DEBUGGER_SOURCE_VIEW_H__
#define __DEBUGGER_SOURCE_VIEW_H__

#include <gtksourceview.h>

G_BEGIN_DECLS

#define DEBUGGER_TYPE_SOURCE_VIEW             (debugger_source_view_get_type ())
#define DEBUGGER_SOURCE_VIEW(obj)             (GTK_CHECK_CAST ((obj), DEBUGGER_TYPE_SOURCE_VIEW, DebuggerSourceView))
#define DEBUGGER_SOURCE_VIEW_CLASS(klass)     (GTK_CHECK_CLASS_CAST ((klass), DEBUGGER_TYPE_SOURCE_VIEW, DebuggerSourceViewClass))
#define DEBUGGER_IS_SOURCE_VIEW(obj)          (GTK_CHECK_TYPE ((obj), DEBUGGER_TYPE_SOURCE_VIEW))
#define DEBUGGER_IS_SOURCE_VIEW_CLASS(klass)  (GTK_CHECK_CLASS_TYPE ((klass), DEBUGGER_TYPE_SOURCE_VIEW))

typedef struct _DebuggerSourceView DebuggerSourceView;
typedef struct _DebuggerSourceViewClass DebuggerSourceViewClass;

struct _DebuggerSourceView
{
        GtkSourceView source_view;

	guint last_popup_x, last_popup_y;
};

struct _DebuggerSourceViewClass
{
        GtkSourceViewClass parent_class;

	void (* debugger_populate_popup)                  (DebuggerSourceView *source_view,
							   GtkMenu            *menu,
							   gint                x,
							   gint                y);
};

GType               debugger_source_view_get_type         (void);
DebuggerSourceView* debugger_source_view_new              (void);
DebuggerSourceView *debugger_source_view_new_with_buffer  (GtkSourceBuffer *buffer);

G_END_DECLS

#endif /* __DEBUGGER_SOURCE_VIEW_H__ */
