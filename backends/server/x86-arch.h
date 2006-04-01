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

static guint64
x86_arch_get_tid (ServerHandle *handle);

static ServerCommandError
server_ptrace_init_after_fork (ServerHandle *handle);

#if defined(__i386__)
#include "i386-arch.h"
#elif defined(__x86_64__)
#include "x86_64-arch.h"
#else
#error "Unknown architecture"
#endif

/* Debug registers' indices.  */
#define DR_NADDR		4  /* the number of debug address registers */
#define DR_STATUS		6  /* index of debug status register (DR6) */
#define DR_CONTROL		7  /* index of debug control register (DR7) */

/* DR7 Debug Control register fields.  */

/* How many bits to skip in DR7 to get to R/W and LEN fields.  */
#define DR_CONTROL_SHIFT	16
/* How many bits in DR7 per R/W and LEN field for each watchpoint.  */
#define DR_CONTROL_SIZE		4

/* Watchpoint/breakpoint read/write fields in DR7.  */
#define DR_RW_EXECUTE		(0x0) /* break on instruction execution */
#define DR_RW_WRITE		(0x1) /* break on data writes */
#define DR_RW_READ		(0x3) /* break on data reads or writes */

/* This is here for completeness.  No platform supports this
   functionality yet (as of Mar-2001).  Note that the DE flag in the
   CR4 register needs to be set to support this.  */
#ifndef DR_RW_IORW
#define DR_RW_IORW		(0x2) /* break on I/O reads or writes */
#endif

/* Watchpoint/breakpoint length fields in DR7.  The 2-bit left shift
   is so we could OR this with the read/write field defined above.  */
#define DR_LEN_1		(0x0 << 2) /* 1-byte region watch or breakpt */
#define DR_LEN_2		(0x1 << 2) /* 2-byte region watch */
#define DR_LEN_4		(0x3 << 2) /* 4-byte region watch */
#define DR_LEN_8		(0x2 << 2) /* 8-byte region watch (x86-64) */

/* Local and Global Enable flags in DR7. */
#define DR_LOCAL_ENABLE_SHIFT	0   /* extra shift to the local enable bit */
#define DR_GLOBAL_ENABLE_SHIFT	1   /* extra shift to the global enable bit */
#define DR_ENABLE_SIZE		2   /* 2 enable bits per debug register */

/* The I'th debug register is vacant if its Local and Global Enable
   bits are reset in the Debug Control register.  */
#define X86_DR_VACANT(arch,i) \
  ((arch->dr_control & (3 << (DR_ENABLE_SIZE * (i)))) == 0)

/* Locally enable the break/watchpoint in the I'th debug register.  */
#define X86_DR_LOCAL_ENABLE(arch,i) \
  arch->dr_control |= (1 << (DR_LOCAL_ENABLE_SHIFT + DR_ENABLE_SIZE * (i)))

/* Globally enable the break/watchpoint in the I'th debug register.  */
#define X86_DR_GLOBAL_ENABLE(arch,i) \
  arch->dr_control |= (1 << (DR_GLOBAL_ENABLE_SHIFT + DR_ENABLE_SIZE * (i)))

/* Disable the break/watchpoint in the I'th debug register.  */
#define X86_DR_DISABLE(arch,i) \
  arch->dr_control &= ~(3 << (DR_ENABLE_SIZE * (i)))

/* Set in DR7 the RW and LEN fields for the I'th debug register.  */
#define X86_DR_SET_RW_LEN(arch,i,rwlen) \
  do { \
    arch->dr_control &= ~(0x0f << (DR_CONTROL_SHIFT+DR_CONTROL_SIZE*(i)));   \
    arch->dr_control |= ((rwlen) << (DR_CONTROL_SHIFT+DR_CONTROL_SIZE*(i))); \
  } while (0)

/* Get from DR7 the RW and LEN fields for the I'th debug register.  */
#define X86_DR_GET_RW_LEN(arch,i) \
  ((arch->dr_control >> (DR_CONTROL_SHIFT + DR_CONTROL_SIZE * (i))) & 0x0f)

/* Did the watchpoint whose address is in the I'th register break?  */
#define X86_DR_WATCH_HIT(arch,i) \
  (arch->dr_status & (1 << (i)))

G_END_DECLS

#endif


