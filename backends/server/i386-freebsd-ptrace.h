#ifndef __MONO_DEBUGGER_I386_FREEBSD_PTRACE_H__
#define __MONO_DEBUGGER_I386_FREEBSD_PTRACE_H__

#include <machine/reg.h>

#define INFERIOR_REGS_TYPE	struct reg
#define INFERIOR_FPREGS_TYPE	struct fpreg

#define INFERIOR_REG_EIP(r)	r.r_eip
#define INFERIOR_REG_ESP(r)	r.r_esp
#define INFERIOR_REG_EBP(r)	r.r_ebp
#define INFERIOR_REG_EAX(r)	r.r_eax
#define INFERIOR_REG_EBX(r)	r.r_ebx
#define INFERIOR_REG_ECX(r)	r.r_ecx
#define INFERIOR_REG_EDX(r)	r.r_edx
#define INFERIOR_REG_ESI(r)	r.r_esi
#define INFERIOR_REG_EDI(r)	r.r_edi
#define INFERIOR_REG_EFLAGS(r)	r.r_eflags
#define INFERIOR_REG_ESP(r)	r.r_esp
#define INFERIOR_REG_FS(r)	r.r_fs
#define INFERIOR_REG_ES(r)	r.r_es
#define INFERIOR_REG_DS(r)	r.r_ds
#define INFERIOR_REG_CS(r)	r.r_cs
#define INFERIOR_REG_SS(r)	r.r_ss
#define INFERIOR_REG_GS(r)	r.r_gs

static ServerCommandError get_registers (InferiorHandle *, INFERIOR_REGS_TYPE *);
static ServerCommandError set_registers (InferiorHandle *, INFERIOR_REGS_TYPE *);
static ServerCommandError get_fp_registers (InferiorHandle *, INFERIOR_FPREGS_TYPE *);
static ServerCommandError set_fp_registers (InferiorHandle *, INFERIOR_FPREGS_TYPE *);

#endif
