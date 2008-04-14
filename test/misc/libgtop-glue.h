#ifndef __MONO_DEBUGGER_LIBGTOP_GLUE_H__
#define __MONO_DEBUGGER_LIBGTOP_GLUE_H__

#include <glib.h>
#include <glibtop.h>
#include <glibtop/procmem.h>
#include <glibtop/procstate.h>
#include <glibtop/procopenfiles.h>

G_BEGIN_DECLS

typedef struct {
	guint64 pagesize;
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
mono_debugger_libgtop_glue_get_memory (glibtop *server, int pid, LibGTopGlueMemoryInfo *info);

gboolean
mono_debugger_libgtop_glue_get_open_files (glibtop *server, int pid, int *result);

gboolean
mono_debugger_libgtop_glue_test (void);

G_END_DECLS

#endif
