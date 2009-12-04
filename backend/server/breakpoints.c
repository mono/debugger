#include <server.h>
#include <breakpoints.h>
#include <glib/gthread.h>
#include <sys/stat.h>
#include <signal.h>
#ifdef HAVE_UNISTD_H
#include <unistd.h>
#endif
#include <string.h>
#include <fcntl.h>
#include <errno.h>

static GStaticRecMutex bpm_mutex = G_STATIC_REC_MUTEX_INIT;

static int last_breakpoint_id = 0;

BreakpointManager *
mono_debugger_breakpoint_manager_new (void)
{
	BreakpointManager *bpm = g_new0 (BreakpointManager, 1);

	bpm->breakpoints = g_ptr_array_new ();
	bpm->breakpoint_hash = g_hash_table_new (NULL, NULL);
	bpm->breakpoint_by_addr = g_hash_table_new (NULL, NULL);

	return bpm;
}

BreakpointManager *
mono_debugger_breakpoint_manager_clone (BreakpointManager *old)
{
	BreakpointManager *bpm = mono_debugger_breakpoint_manager_new ();
	int i;

	for (i = 0; i < old->breakpoints->len; i++) {
		BreakpointInfo *old_info = g_ptr_array_index (old->breakpoints, i);
		BreakpointInfo *info = g_memdup (old_info, sizeof (BreakpointInfo));

		mono_debugger_breakpoint_manager_insert (bpm, info);
	}

	return bpm;
}

void
mono_debugger_breakpoint_manager_free (BreakpointManager *bpm)
{
	g_ptr_array_free (bpm->breakpoints, TRUE);
	g_hash_table_destroy (bpm->breakpoint_hash);
	g_hash_table_destroy (bpm->breakpoint_by_addr);
	g_free (bpm);
}

void
mono_debugger_breakpoint_manager_lock (void)
{
	g_static_rec_mutex_lock (&bpm_mutex);
}

void
mono_debugger_breakpoint_manager_unlock (void)
{
	g_static_rec_mutex_unlock (&bpm_mutex);
}

void
mono_debugger_breakpoint_manager_insert (BreakpointManager *bpm, BreakpointInfo *breakpoint)
{
	g_ptr_array_add (bpm->breakpoints, breakpoint);
	g_hash_table_insert (bpm->breakpoint_hash, GSIZE_TO_POINTER (breakpoint->id), breakpoint);
	g_hash_table_insert (bpm->breakpoint_by_addr, GSIZE_TO_POINTER (breakpoint->address), breakpoint);
}

BreakpointInfo *
mono_debugger_breakpoint_manager_lookup (BreakpointManager *bpm, guint64 address)
{
	return g_hash_table_lookup (bpm->breakpoint_by_addr, GSIZE_TO_POINTER (address));
}

BreakpointInfo *
mono_debugger_breakpoint_manager_lookup_by_id (BreakpointManager *bpm, guint32 id)
{
	return g_hash_table_lookup (bpm->breakpoint_hash, GSIZE_TO_POINTER (id));
}

GPtrArray *
mono_debugger_breakpoint_manager_get_breakpoints (BreakpointManager *bpm)
{
	return bpm->breakpoints;
}

void
mono_debugger_breakpoint_manager_remove (BreakpointManager *bpm, BreakpointInfo *breakpoint)
{
	if (!mono_debugger_breakpoint_manager_lookup_by_id (bpm, breakpoint->id)) {
		g_warning (G_STRLOC ": mono_debugger_breakpoint_manager_remove(): No such breakpoint %d", breakpoint->id);
		return;
	}

	if (--breakpoint->refcount > 0)
		return;

	g_hash_table_remove (bpm->breakpoint_hash, GSIZE_TO_POINTER (breakpoint->id));
	g_hash_table_remove (bpm->breakpoint_by_addr, GSIZE_TO_POINTER (breakpoint->address));
	g_ptr_array_remove_fast (bpm->breakpoints, breakpoint);
	g_free (breakpoint);
}

int
mono_debugger_breakpoint_manager_get_next_id (void)
{
	return ++last_breakpoint_id;
}

int
mono_debugger_breakpoint_info_get_id (BreakpointInfo *info)
{
	return info->id;
}

gboolean
mono_debugger_breakpoint_info_get_is_enabled (BreakpointInfo *info)
{
	return info->enabled;
}
