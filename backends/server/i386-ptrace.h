#ifndef __MONO_DEBUGGER_I386_PTRACE_H__
#define __MONO_DEBUGGER_I386_PTRACE_H__

ServerCommandError _mono_debugger_server_get_registers (InferiorHandle *, INFERIOR_REGS_TYPE *);
ServerCommandError _mono_debugger_server_set_registers (InferiorHandle *, INFERIOR_REGS_TYPE *);
ServerCommandError _mono_debugger_server_get_fp_registers (InferiorHandle *, INFERIOR_FPREGS_TYPE *);
ServerCommandError _mono_debugger_server_set_fp_registers (InferiorHandle *, INFERIOR_FPREGS_TYPE *);
ServerCommandError _mono_debugger_server_set_dr (InferiorHandle *handle, int regnum, unsigned long value);
int _mono_debugger_server_wait (InferiorHandle *inferior);
void _mono_debugger_server_setup_inferior (ServerHandle *handle);

#endif
