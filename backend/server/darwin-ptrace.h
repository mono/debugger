#ifndef __MONO_DEBUGGER_MACHO_PTRACE_H__
#define __MONO_DEBUGGER_MACHO_PTRACE_H__

#include <mach/mach.h>

#include "x86-arch.h"

struct OSData
{
	mach_port_t task;
	mach_port_t exception_port;
	thread_t thread;
	pthread_t exception_thread;
	int stop_exception_thread;
	int thread_index;
	int is_stopped;
};

#define PTRACE_KILL PT_KILL

struct user_regs_struct {
	long eax, ebx, ecx, edx;
	long edi, esi, ebp, esp;
	unsigned short ss, xss;
	long eflags;
	long eip;
	unsigned short cs, xcs;
	unsigned short ds, xds, es, xes;
	unsigned short fs, xfs, gs, xgs;
	long orig_eax;
};

struct user_fpregs_struct {
	i386_float_state_t regs;
};

#include "x86-ptrace.h"

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

static ServerCommandError
_server_ptrace_make_memory_executable (ServerHandle *handle, guint64 start, guint32 size);

#define GET_PID(x) ((x)&0x1ffff)
#define GET_THREAD_INDEX(x) ((x)>>17)
#define COMPOSED_PID(pid, ti) ((pid) | ((ti)<<17))

thread_t get_thread_from_index(int th_index);
ServerCommandError get_thread_index(mach_port_t task, thread_t thread, int *index);
void* server_mach_msg_rcv_thread(void *p);

thread_t
get_application_thread_port (mach_port_t task, thread_t our_name);

#endif
