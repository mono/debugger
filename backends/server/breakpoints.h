#ifndef __MONO_DEBUGGER_BREAKPOINTS_H__
#define __MONO_DEBUGGER_BREAKPOINTS_H__

#include <glib.h>

G_BEGIN_DECLS

typedef void (*BreakpointManagerMutexFunc) (void);

typedef struct {
	GPtrArray *breakpoints;
	GHashTable *breakpoint_hash;
	GHashTable *breakpoint_by_addr;
	BreakpointManagerMutexFunc lock_func;
	BreakpointManagerMutexFunc unlock_func;
} BreakpointManager;

typedef struct {
	int id;
	int owner;
	int refcount;
	int enabled;
	int is_hardware_bpt;
	guint64 address;
} BreakpointInfo;

BreakpointManager *
mono_debugger_breakpoint_manager_new                 (BreakpointManagerMutexFunc lock_func,
						      BreakpointManagerMutexFunc unlock_func);

void
mono_debugger_breakpoint_manager_free                (BreakpointManager *bpm);

void
mono_debugger_breakpoint_manager_lock                (BreakpointManager *bpm);

void
mono_debugger_breakpoint_manager_unlock              (BreakpointManager *bpm);

int
mono_debugger_breakpoint_manager_get_next_id         (void);

void
mono_debugger_breakpoint_manager_insert              (BreakpointManager *bpm, BreakpointInfo *breakpoint);

BreakpointInfo *
mono_debugger_breakpoint_manager_lookup              (BreakpointManager *bpm, guint64 address);

BreakpointInfo *
mono_debugger_breakpoint_manager_lookup_by_id        (BreakpointManager *bpm, guint32 id);

GPtrArray *
mono_debugger_breakpoint_manager_get_breakpoints     (BreakpointManager *bpm);

void
mono_debugger_breakpoint_manager_remove              (BreakpointManager *bpm, BreakpointInfo *breakpoint);

int
mono_debugger_breakpoint_info_get_id                 (BreakpointInfo *info);

int
mono_debugger_breakpoint_info_get_owner              (BreakpointInfo *info);

G_END_DECLS

#endif
