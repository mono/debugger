#ifndef __MONO_DEBUGGER_X86_64_PTRACE_H__
#define __MONO_DEBUGGER_X86_64_PTRACE_H__

typedef struct OSData OSData;

struct InferiorHandle
{
	OSData os;

	guint32 pid;
	int stepping;
	int last_signal;
	int redirect_fds;
	int output_fd [2], error_fd [2];
	int is_thread;
};

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

static ServerCommandError
_server_ptrace_check_errno (InferiorHandle *);

static ServerCommandError
_server_ptrace_make_memory_executable (ServerHandle *handle, guint64 start, guint32 size);

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
server_ptrace_write_memory (ServerHandle *handle, guint64 start,
			    guint32 size, gconstpointer buffer);

static ServerCommandError
server_ptrace_poke_word (ServerHandle *handle, guint64 addr, gsize value);

static ServerCommandError
_server_ptrace_set_dr (InferiorHandle *handle, int regnum, guint64 value);

static ServerCommandError
_server_ptrace_get_dr (InferiorHandle *handle, int regnum, guint64 *value);

static ServerCommandError
server_ptrace_continue (ServerHandle *handle);

static ServerCommandError
server_ptrace_step (ServerHandle *handle);

static ServerCommandError
server_ptrace_kill (ServerHandle *handle);

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

#endif
