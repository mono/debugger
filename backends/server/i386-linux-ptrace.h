#ifndef __MONO_DEBUGGER_I386_LINUX_PTRACE_H__
#define __MONO_DEBUGGER_I386_LINUX_PTRACE_H__

#include <asm/user.h>

#define INFERIOR_REGS_TYPE	struct user_regs_struct
#define INFERIOR_FPREGS_TYPE	struct user_i387_struct

#define INFERIOR_REG_EIP(r)	r.eip
#define INFERIOR_REG_ESP(r)	r.esp
#define INFERIOR_REG_EBP(r)	r.ebp
#define INFERIOR_REG_EAX(r)	r.eax
#define INFERIOR_REG_EBX(r)	r.ebx
#define INFERIOR_REG_ECX(r)	r.ecx
#define INFERIOR_REG_EDX(r)	r.edx
#define INFERIOR_REG_ESI(r)	r.esi
#define INFERIOR_REG_EDI(r)	r.edi
#define INFERIOR_REG_EFLAGS(r)	r.eflags
#define INFERIOR_REG_ESP(r)	r.esp
#define INFERIOR_REG_FS(r)	r.fs
#define INFERIOR_REG_ES(r)	r.es
#define INFERIOR_REG_DS(r)	r.ds
#define INFERIOR_REG_CS(r)	r.cs
#define INFERIOR_REG_SS(r)	r.ss
#define INFERIOR_REG_GS(r)	r.gs

static ServerCommandError
_server_ptrace_get_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs);

static ServerCommandError
_server_ptrace_set_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs);

static ServerCommandError
_server_ptrace_get_fp_registers (InferiorHandle *inferior, INFERIOR_FPREGS_TYPE *regs);

static ServerCommandError
_server_ptrace_set_fp_registers (InferiorHandle *inferior, INFERIOR_FPREGS_TYPE *regs);

static ServerCommandError
server_ptrace_read_memory (ServerHandle *handle, guint64 start, guint32 size, gpointer buffer);

static ServerCommandError
_server_ptrace_set_dr (InferiorHandle *handle, int regnum, unsigned long value);

static ServerCommandError
server_ptrace_stop (ServerHandle *handle);

static ServerCommandError
server_ptrace_stop_and_wait (ServerHandle *handle, guint32 *status);

static void
_server_ptrace_setup_inferior (ServerHandle *handle, gboolean is_main);

static gboolean
_server_ptrace_setup_thread_manager (ServerHandle *handle);

static ServerCommandError
server_ptrace_get_signal_info (ServerHandle *handle, SignalInfo *sinfo);

static int
do_wait (int pid, guint32 *status);

#include "i386-ptrace.h"

#endif
