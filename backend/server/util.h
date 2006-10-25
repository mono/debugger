#ifndef __MONO_DEBUGGER_SERVER_UTIL_H__
#define __MONO_DEBUGGER_SERVER_UTIL_H__

#include <glib.h>

G_BEGIN_DECLS

/*
 * Utility functions.
 */

extern gboolean
mono_debugger_util_read              (int                      fd,
				      gpointer                 data,
				      int                      size);

extern gboolean
mono_debugger_util_write             (int                      fd,
				      gconstpointer            data,
				      int                      size);

G_END_DECLS

#endif
