#ifndef __MONO_DEBUGGER_I386_PTRACE_H__
#define __MONO_DEBUGGER_I386_PTRACE_H__

#ifndef PTRACE_SETOPTIONS
#define PTRACE_SETOPTIONS	0x4200
#endif
#ifndef PTRACE_GETEVENTMSG
#define PTRACE_GETEVENTMSG	0x4201
#endif

#ifndef PTRACE_EVENT_FORK

/* options set using PTRACE_SETOPTIONS */
#define PTRACE_O_TRACESYSGOOD	0x00000001
#define PTRACE_O_TRACEFORK	0x00000002
#define PTRACE_O_TRACEVFORK	0x00000004
#define PTRACE_O_TRACECLONE	0x00000008
#define PTRACE_O_TRACEEXEC	0x00000010
#define PTRACE_O_TRACEVFORKDONE	0x00000020
#define PTRACE_O_TRACEEXIT	0x00000040

/* Wait extended result codes for the above trace options.  */
#define PTRACE_EVENT_FORK	1
#define PTRACE_EVENT_VFORK	2
#define PTRACE_EVENT_CLONE	3
#define PTRACE_EVENT_EXEC	4
#define PTRACE_EVENT_VFORKDONE	5
#define PTRACE_EVENT_EXIT	6

#endif /* PTRACE_EVENT_FORK */

ServerCommandError _server_ptrace_get_registers (InferiorHandle *, INFERIOR_REGS_TYPE *);
ServerCommandError _server_ptrace_set_registers (InferiorHandle *, INFERIOR_REGS_TYPE *);
ServerCommandError _server_ptrace_get_fp_registers (InferiorHandle *, INFERIOR_FPREGS_TYPE *);
ServerCommandError _server_ptrace_set_fp_registers (InferiorHandle *, INFERIOR_FPREGS_TYPE *);
void _server_ptrace_setup_inferior (ServerHandle *handle, gboolean is_main);
gboolean _server_ptrace_has_thread_manager (ServerHandle *handle);
gboolean _server_ptrace_setup_thread_manager (ServerHandle *handle);

extern pthread_t mono_debugger_thread;
extern int pending_sigint;

#endif
