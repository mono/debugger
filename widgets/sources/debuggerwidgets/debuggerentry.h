#ifndef __DEBUGGER_ENTRY_H__
#define __DEBUGGER_ENTRY_H__

#include <gtk/gtkentry.h>

G_BEGIN_DECLS

#define DEBUGGER_TYPE_ENTRY             (debugger_entry_get_type ())
#define DEBUGGER_ENTRY(obj)             (GTK_CHECK_CAST ((obj), DEBUGGER_TYPE_ENTRY, DebuggerEntry))
#define DEBUGGER_ENTRY_CLASS(klass)     (GTK_CHECK_CLASS_CAST ((klass), DEBUGGER_TYPE_ENTRY, DebuggerEntryClass))
#define DEBUGGER_IS_ENTRY(obj)          (GTK_CHECK_TYPE ((obj), DEBUGGER_TYPE_ENTRY))
#define DEBUGGER_IS_ENTRY_CLASS(klass)  (GTK_CHECK_CLASS_TYPE ((klass), DEBUGGER_TYPE_ENTRY))

typedef struct _DebuggerEntry DebuggerEntry;
typedef struct _DebuggerEntryClass DebuggerEntryClass;

struct _DebuggerEntry
{
        GtkEntry entry;
};

struct _DebuggerEntryClass
{
        GtkEntryClass parent_class;

        void (* previous_line)                 (DebuggerEntry  *entry);
        void (* next_line)                     (DebuggerEntry  *entry);
};

GType          debugger_entry_get_type         ();
DebuggerEntry* debugger_entry_new              (void);

G_END_DECLS

#endif /* __DEBUGGER_ENTRY_H__ */
