#define _GNU_SOURCE
#include <server.h>
#include <breakpoints.h>
#include <sys/stat.h>
#include <sys/ptrace.h>
#include <sys/wait.h>
#include <signal.h>
#include <unistd.h>
#include <string.h>
#include <fcntl.h>
#include <errno.h>

static int last_breakpoint_id = 0;

BreakpointManager *
mono_debugger_breakpoint_manager_new (BreakpointManagerMutexFunc lock_func,
				      BreakpointManagerMutexFunc unlock_func)
{
	BreakpointManager *bpm = g_new0 (BreakpointManager, 1);

	bpm->breakpoints = g_ptr_array_new ();
	bpm->breakpoint_hash = g_hash_table_new (NULL, NULL);
	bpm->breakpoint_by_addr = g_hash_table_new (NULL, NULL);
	bpm->lock_func = lock_func;
	bpm->unlock_func = unlock_func;

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
mono_debugger_breakpoint_manager_lock (BreakpointManager *bpm)
{
	(* bpm->lock_func) ();
}

void
mono_debugger_breakpoint_manager_unlock (BreakpointManager *bpm)
{
	(* bpm->unlock_func) ();
}

void
mono_debugger_breakpoint_manager_insert (BreakpointManager *bpm, BreakpointInfo *breakpoint)
{
	g_ptr_array_add (bpm->breakpoints, breakpoint);
	g_hash_table_insert (bpm->breakpoint_hash, GUINT_TO_POINTER (breakpoint->id), breakpoint);
	if (!breakpoint->is_hardware_bpt)
		g_hash_table_insert (bpm->breakpoint_by_addr, (gpointer) breakpoint->address, breakpoint);
}

BreakpointInfo *
mono_debugger_breakpoint_manager_lookup (BreakpointManager *bpm, guint64 address)
{
	g_message (G_STRLOC ": %p - %p", bpm, bpm->breakpoint_by_addr);
	return g_hash_table_lookup (bpm->breakpoint_by_addr, GUINT_TO_POINTER (address));
}

BreakpointInfo *
mono_debugger_breakpoint_manager_lookup_by_id (BreakpointManager *bpm, guint32 id)
{
	return g_hash_table_lookup (bpm->breakpoint_hash, GUINT_TO_POINTER (id));
}

GPtrArray *
mono_debugger_breakpoint_manager_get_breakpoints (BreakpointManager *bpm)
{
	return bpm->breakpoints;
}

void
mono_debugger_breakpoint_manager_remove (BreakpointManager *bpm, BreakpointInfo *breakpoint)
{
	if (--breakpoint->refcount > 0)
		return;

	g_hash_table_remove (bpm->breakpoint_hash, GUINT_TO_POINTER (breakpoint->id));
	if (!breakpoint->is_hardware_bpt)
		g_hash_table_remove (bpm->breakpoint_by_addr, (gpointer) breakpoint->address);
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

int
mono_debugger_breakpoint_info_get_owner (BreakpointInfo *info)
{
	return info->owner;
}
