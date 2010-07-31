#ifndef __MONO_DEBUGGER_LIBGTOP_GLUE_H__
#define __MONO_DEBUGGER_LIBGTOP_GLUE_H__

#include <glib.h>

G_BEGIN_DECLS

typedef struct {
	guint64 size;
	guint64 vsize;
	guint64 resident;
	guint64 share;
	guint64 rss;
	guint64 rss_rlim;
} LibGTopGlueMemoryInfo;

int
mono_debugger_libgtop_glue_get_pid (void);

gboolean
mono_debugger_libgtop_glue_get_memory (int pid, LibGTopGlueMemoryInfo *info);

gboolean
mono_debugger_libgtop_glue_get_open_files (int pid, int *result);

G_END_DECLS

#endif
