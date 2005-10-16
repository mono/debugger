#ifndef __MONO_DEBUGGER_X86_ARCH_H__
#define __MONO_DEBUGGER_X86_ARCH_H__

#include <glib.h>

G_BEGIN_DECLS

typedef struct {
	BreakpointInfo info;
	int dr_index;
	char saved_insn;
} X86BreakpointInfo;

typedef enum {
	STOP_ACTION_SEND_STOPPED,
	STOP_ACTION_BREAKPOINT_HIT,
	STOP_ACTION_CALLBACK,
	STOP_ACTION_CALLBACK_COMPLETED,
	STOP_ACTION_NOTIFICATION
} ChildStoppedAction;

static ArchInfo *
x86_arch_initialize (void);

static void
x86_arch_finalize (ArchInfo *arch);

static void
x86_arch_remove_breakpoints_from_target_memory (ServerHandle *handle, guint64 start,
						guint32 size, gpointer buffer);

static ChildStoppedAction
x86_arch_child_stopped (ServerHandle *handle, int stopsig,
			guint64 *callback_arg, guint64 *retval, guint64 *retval2);

static ServerCommandError
x86_arch_get_registers (ServerHandle *handle);

static guint32
x86_arch_get_tid (ServerHandle *handle);

#if defined(__i386__)
#include "i386-arch.h"
#elif defined(__x86_64__)
#include "x86_64-arch.h"
#else
#error "Unknown architecture"
#endif

G_END_DECLS

#endif


