#ifndef __MONO_DEBUGGER_X86_86_ARCH_H__
#define __MONO_DEBUGGER_X86_86_ARCH_H__

#include <glib.h>

G_BEGIN_DECLS

#if defined(__x86_64__)

#include <asm/user.h>

#define INFERIOR_REGS_TYPE	struct user_regs_struct
#define INFERIOR_FPREGS_TYPE	struct user_i387_struct

#define INFERIOR_REG_R15(r)	r.r15
#define INFERIOR_REG_R14(r)	r.r14
#define INFERIOR_REG_R13(r)	r.r13
#define INFERIOR_REG_R12(r)	r.r12
#define INFERIOR_REG_RBP(r)	r.rbp
#define INFERIOR_REG_RBX(r)	r.rbx
#define INFERIOR_REG_R11(r)	r.r11
#define INFERIOR_REG_R10(r)	r.r10
#define INFERIOR_REG_R9(r)	r.r9
#define INFERIOR_REG_R8(r)	r.r8
#define INFERIOR_REG_RAX(r)	r.rax
#define INFERIOR_REG_RCX(r)	r.rcx
#define INFERIOR_REG_RDX(r)	r.rdx
#define INFERIOR_REG_RSI(r)	r.rsi
#define INFERIOR_REG_RDI(r)	r.rdi
#define INFERIOR_REG_ORIG_RAX(r)	r.orig_rax
#define INFERIOR_REG_RIP(r)	r.rip
#define INFERIOR_REG_CS(r)	r.cs
#define INFERIOR_REG_EFLAGS(r)	r.eflags
#define INFERIOR_REG_RSP(r)	r.rsp
#define INFERIOR_REG_SS(r)	r.ss

#define INFERIOR_REG_FS_BASE(r)	r.fs_base
#define INFERIOR_REG_GS_BASE(r)	r.gs_base

#define INFERIOR_REG_DS(r)	r.ds
#define INFERIOR_REG_ES(r)	r.es
#define INFERIOR_REG_FS(r)	r.fs
#define INFERIOR_REG_GS(r)	r.gs

#else
#error "Unknown architecture"
#endif

G_END_DECLS

#endif


