#ifndef __MONO_DEBUGGER_POWERPC_ARCH_H__
#define __MONO_DEBUGGER_POWERPC_ARCH_H__

#include <mach/ppc/thread_status.h>
#include <mach/ppc/task.h>
#include <glib.h>

G_BEGIN_DECLS

#define INFERIOR_REG_R0(r)	r.r0
#define INFERIOR_REG_R1(r)	r.r1
#define INFERIOR_REG_R2(r)	r.r2
#define INFERIOR_REG_R3(r)	r.r3
#define INFERIOR_REG_R4(r)	r.r4
#define INFERIOR_REG_R5(r)	r.r5
#define INFERIOR_REG_R6(r)	r.r6
#define INFERIOR_REG_R7(r)	r.r7
#define INFERIOR_REG_R8(r)	r.r8
#define INFERIOR_REG_R9(r)	r.r9
#define INFERIOR_REG_R10(r)	r.r10
#define INFERIOR_REG_R11(r)	r.r11
#define INFERIOR_REG_R12(r)	r.r12
#define INFERIOR_REG_R13(r)	r.r13
#define INFERIOR_REG_R14(r)	r.r14
#define INFERIOR_REG_R15(r)	r.r15
#define INFERIOR_REG_R16(r)	r.r16
#define INFERIOR_REG_R17(r)	r.r17
#define INFERIOR_REG_R18(r)	r.r18
#define INFERIOR_REG_R19(r)	r.r19
#define INFERIOR_REG_R20(r)	r.r20
#define INFERIOR_REG_R21(r)	r.r21
#define INFERIOR_REG_R22(r)	r.r22
#define INFERIOR_REG_R23(r)	r.r23
#define INFERIOR_REG_R24(r)	r.r24
#define INFERIOR_REG_R25(r)	r.r25
#define INFERIOR_REG_R26(r)	r.r26
#define INFERIOR_REG_R27(r)	r.r27
#define INFERIOR_REG_R28(r)	r.r28
#define INFERIOR_REG_R29(r)	r.r29
#define INFERIOR_REG_R30(r)	r.r30
#define INFERIOR_REG_R31(r)	r.r31

#define INFERIOR_REG_PC(r)	r.srr0
#define INFERIOR_REG_PS(r)	r.srr1
#define INFERIOR_REG_CR(r)	r.cr
#define INFERIOR_REG_LR(r)	r.lr
#define INFERIOR_REG_CTR(r)	r.ctr
#define INFERIOR_REG_XER(r)	r.xer
#define INFERIOR_REG_MQ(r)	r.mq
#define INFERIOR_REG_VRSAVE(r)	r.vrsave

typedef enum {
	DEBUGGER_REG_R0	= 0,
	DEBUGGER_REG_R1,
	DEBUGGER_REG_R2,
	DEBUGGER_REG_R3,
	DEBUGGER_REG_R4,
	DEBUGGER_REG_R5,
	DEBUGGER_REG_R6,
	DEBUGGER_REG_R7,
	DEBUGGER_REG_R8,
	DEBUGGER_REG_R9,
	DEBUGGER_REG_R10,
	DEBUGGER_REG_R11,
	DEBUGGER_REG_R12,
	DEBUGGER_REG_R13,
	DEBUGGER_REG_R14,
	DEBUGGER_REG_R15,
	DEBUGGER_REG_R16,
	DEBUGGER_REG_R17,
	DEBUGGER_REG_R18,
	DEBUGGER_REG_R19,
	DEBUGGER_REG_R20,
	DEBUGGER_REG_R21,
	DEBUGGER_REG_R22,
	DEBUGGER_REG_R23,
	DEBUGGER_REG_R24,
	DEBUGGER_REG_R25,
	DEBUGGER_REG_R26,
	DEBUGGER_REG_R27,
	DEBUGGER_REG_R28,
	DEBUGGER_REG_R29,
	DEBUGGER_REG_R30,
	DEBUGGER_REG_R31,

	DEBUGGER_REG_PC,
	DEBUGGER_REG_PS,
	DEBUGGER_REG_CR,
	DEBUGGER_REG_LR,
	DEBUGGER_REG_CTR,
	DEBUGGER_REG_XER,
	DEBUGGER_REG_MQ,
	DEBUGGER_REG_VRSAVE,

	DEBUGGER_REG_LAST
} DebuggerPowerPCRegisters;

typedef struct {
	BreakpointInfo info;
	char saved_insn [4];
} PowerPcBreakpointInfo;

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

static ChildStoppedAction
powerpc_arch_child_stopped (ServerHandle *handle, int stopsig,
			    guint64 *callback_arg, guint64 *retval, guint64 *retval2);

G_END_DECLS

#endif


