#ifndef __MONO_DEBUGGER_X86_LINUX_PTRACE_H__
#define __MONO_DEBUGGER_X86_LINUX_PTRACE_H__

#include "x86-arch.h"

static ServerCommandError
_server_ptrace_check_errno (InferiorHandle *);

static ServerCommandError
_server_ptrace_get_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs);

static ServerCommandError
_server_ptrace_set_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs);

static ServerCommandError
_server_ptrace_get_fp_registers (InferiorHandle *inferior, INFERIOR_FPREGS_TYPE *regs);

static ServerCommandError
_server_ptrace_set_fp_registers (InferiorHandle *inferior, INFERIOR_FPREGS_TYPE *regs);

static ServerCommandError
_server_ptrace_read_memory (ServerHandle *handle, guint64 start,
			    guint32 size, gpointer buffer);

static ServerCommandError
server_ptrace_read_memory (ServerHandle *handle, guint64 start, guint32 size, gpointer buffer);

static ServerCommandError
_server_ptrace_set_dr (InferiorHandle *handle, int regnum, guint64 value);

static ServerCommandError
_server_ptrace_get_dr (InferiorHandle *handle, int regnum, guint64 *value);

static ServerCommandError
server_ptrace_stop (ServerHandle *handle);

static ServerCommandError
server_ptrace_stop_and_wait (ServerHandle *handle, guint32 *status);

static ServerCommandError
_server_ptrace_setup_inferior (ServerHandle *handle);

static void
_server_ptrace_finalize_inferior (ServerHandle *handle);

static ServerCommandError
server_ptrace_get_signal_info (ServerHandle *handle, SignalInfo **sinfo);

static gboolean
_server_ptrace_wait_for_new_thread (ServerHandle *handle);

#include "x86-ptrace.h"

#endif
