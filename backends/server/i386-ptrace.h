#ifndef __MONO_DEBUGGER_I386_PTRACE_H__
#define __MONO_DEBUGGER_I386_PTRACE_H__

static ServerCommandError server_get_registers (InferiorHandle *, INFERIOR_REGS_TYPE *);
static ServerCommandError server_set_registers (InferiorHandle *, INFERIOR_REGS_TYPE *);
static ServerCommandError server_get_fp_registers (InferiorHandle *, INFERIOR_FPREGS_TYPE *);
static ServerCommandError server_set_fp_registers (InferiorHandle *, INFERIOR_FPREGS_TYPE *);
static ServerCommandError server_set_dr (InferiorHandle *handle, int regnum, unsigned long value);
static void server_setup_inferior (InferiorHandle *, ArchInfo *arch);
static int server_do_wait (InferiorHandle *handle);


static ServerCommandError
server_peek_word (InferiorHandle *handle, ArchInfo *arch, guint64 start, int *retval);

static ServerCommandError
server_ptrace_read_data (InferiorHandle *handle, ArchInfo *arch, guint64 start,
			 guint32 size, gpointer buffer);

#endif
