#ifndef __MONO_DEBUGGER_I386_WIN32_H__
#define __MONO_DEBUGGER_I386_WIN32_H__

#include <w32api.h>
#include <windows.h>

#define INFERIOR_REGS_TYPE	CONTEXT
#define INFERIOR_FPREGS_TYPE	CONTEXT

#define FLAG_TRACE_BIT		0x100

#define INFERIOR_REG_EIP(r)	r.Eip
#define INFERIOR_REG_ESP(r)	r.Esp
#define INFERIOR_REG_EBP(r)	r.Ebp
#define INFERIOR_REG_EAX(r)	r.Eax
#define INFERIOR_REG_EBX(r)	r.Ebx
#define INFERIOR_REG_ECX(r)	r.Ecx
#define INFERIOR_REG_EDX(r)	r.Edx
#define INFERIOR_REG_ESI(r)	r.Esi
#define INFERIOR_REG_EDI(r)	r.Edi
#define INFERIOR_REG_EFLAGS(r)	r.EFlags
#define INFERIOR_REG_ESP(r)	r.Esp
#define INFERIOR_REG_FS(r)	r.SegFs
#define INFERIOR_REG_ES(r)	r.SegEs
#define INFERIOR_REG_DS(r)	r.SegDs
#define INFERIOR_REG_CS(r)	r.SegCs
#define INFERIOR_REG_SS(r)	r.SegSs
#define INFERIOR_REG_GS(r)	r.SegGs

static ServerCommandError server_get_registers (InferiorHandle *, INFERIOR_REGS_TYPE *);
static ServerCommandError server_set_registers (InferiorHandle *, INFERIOR_REGS_TYPE *);
static ServerCommandError server_get_fp_registers (InferiorHandle *, INFERIOR_FPREGS_TYPE *);
static ServerCommandError server_set_fp_registers (InferiorHandle *, INFERIOR_FPREGS_TYPE *);
static ServerCommandError server_set_dr (InferiorHandle *handle, int regnum, unsigned long value);

static ServerCommandError
server_peek_word (InferiorHandle *handle, ArchInfo *arch, guint64 start, int *retval);

static void
server_win32_wait (InferiorHandle *handle, ArchInfo *arch, ServerStatusMessageType *type,
		   guint64 *arg, guint64 *data1, guint64 *data2);

#endif
