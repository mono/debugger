#ifndef __MONO_DEBUGGER_POWERPC_ARCH_H__
#define __MONO_DEBUGGER_POWERPC_ARCH_H__

#include <mach/ppc/thread_status.h>
#include <mach/ppc/task.h>
#include <glib.h>

G_BEGIN_DECLS

#define INFERIOR_REG_EIP(r)	r.srr0

typedef enum {
	DEBUGGER_REG_EBX = 0,
	DEBUGGER_REG_ECX,
	DEBUGGER_REG_EDX,
	DEBUGGER_REG_ESI,
	DEBUGGER_REG_EDI,
	DEBUGGER_REG_EBP,
	DEBUGGER_REG_EAX,
	DEBUGGER_REG_DS,
	DEBUGGER_REG_ES,
	DEBUGGER_REG_FS,
	DEBUGGER_REG_GS,
	DEBUGGER_REG_EIP,
	DEBUGGER_REG_CS,
	DEBUGGER_REG_EFLAGS,
	DEBUGGER_REG_ESP,
	DEBUGGER_REG_SS,

	DEBUGGER_REG_LAST
} DebuggerPowerPCRegisters;

typedef enum {
	STOP_ACTION_SEND_STOPPED,
	STOP_ACTION_BREAKPOINT_HIT,
	STOP_ACTION_CALLBACK
} ChildStoppedAction;

#define INFERIOR_REGS_TYPE	ppc_thread_state_t

static ArchInfo *
powerpc_arch_initialize (void);

static void
powerpc_arch_finalize (ArchInfo *arch);

static ServerCommandError
powerpc_arch_get_registers (ServerHandle *handle);

G_END_DECLS

#endif


