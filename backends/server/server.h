#ifndef __MONO_DEBUGGER_SERVER_H__
#define __MONO_DEBUGGER_SERVER_H__

#include <breakpoints.h>
#include <signal.h>
#include <glib.h>

G_BEGIN_DECLS

typedef enum {
	COMMAND_ERROR_NONE = 0,
	COMMAND_ERROR_UNKNOWN,
	COMMAND_ERROR_NO_INFERIOR,
	COMMAND_ERROR_ALREADY_HAVE_INFERIOR,
	COMMAND_ERROR_FORK,
	COMMAND_ERROR_NOT_STOPPED,
	COMMAND_ERROR_ALREADY_STOPPED,
	COMMAND_ERROR_RECURSIVE_CALL,
	COMMAND_ERROR_NO_SUCH_BREAKPOINT,
	COMMAND_ERROR_UNKNOWN_REGISTER,
	COMMAND_ERROR_DR_OCCUPIED,
	COMMAND_ERROR_MEMORY_ACCESS
} ServerCommandError;

typedef enum {
	MESSAGE_NONE,
	MESSAGE_UNKNOWN_ERROR = 1,
	MESSAGE_CHILD_EXITED = 2,
	MESSAGE_CHILD_STOPPED,
	MESSAGE_CHILD_SIGNALED,
	MESSAGE_CHILD_CALLBACK,
	MESSAGE_CHILD_HIT_BREAKPOINT,
	MESSAGE_CHILD_MEMORY_CHANGED,
	MESSAGE_CHILD_CREATED_THREAD
} ServerStatusMessageType;

typedef struct {
	ServerStatusMessageType type;
	guint32 arg;
} ServerStatusMessage;

typedef struct {
	guint64 address;
	guint64 frame_address;
} StackFrame;

/* This is an opaque data structure which the backend may use to store stuff. */
typedef struct InferiorHandle InferiorHandle;
typedef struct ArchInfo ArchInfo;

/* C# delegates. */
typedef void (*ChildOutputFunc) (const char *output);

typedef struct {
	int sigkill;
	int sigstop;
	int sigint;
	int sigchld;

	int sigprof;
	int sigpwr;
	int sigxcpu;

	int thread_abort;
	int thread_restart;
	int thread_debug;
	int mono_thread_debug;
} SignalInfo;

/*
 * Library functions.
 *
 * These functions just call the corresponding function in the ServerHandle's vtable.
 * They're just here to be called from C#.
 */

typedef struct {
	ArchInfo *arch;
	InferiorHandle *inferior;
	BreakpointManager *bpm;
} ServerHandle;

void
mono_debugger_server_static_init          (void);

ServerHandle *
mono_debugger_server_initialize           (BreakpointManager  *bpm);

ServerCommandError
mono_debugger_server_spawn                (ServerHandle       *handle,
					   const gchar        *working_directory,
					   gchar             **argv,
					   gchar             **envp,
					   gint               *child_pid,
					   ChildOutputFunc     stdout_handler,
					   ChildOutputFunc     stderr_handler,
					   gchar             **error);

ServerCommandError
mono_debugger_server_attach               (ServerHandle       *handle,
					   guint32             pid,
					   guint32            *tid);

void
mono_debugger_server_finalize             (ServerHandle       *handle);

guint32
mono_debugger_server_global_wait          (guint64                 *status);

ServerStatusMessageType
mono_debugger_server_dispatch_event       (ServerHandle            *handle,
					   guint64                  status,
					   guint64                 *arg,
					   guint64                 *data1,
					   guint64                 *data2);

ServerCommandError
mono_debugger_server_get_target_info      (ServerHandle       *handle,
					   guint32            *target_int_size,
					   guint32            *target_long_size,
					   guint32            *target_address_size);

ServerCommandError
mono_debugger_server_get_pc               (ServerHandle       *handle,
					   guint64            *pc);

ServerCommandError
mono_debugger_server_current_insn_is_bpt  (ServerHandle       *handle,
					   guint32            *is_breakpoint);

ServerCommandError
mono_debugger_server_step                 (ServerHandle       *handle);

ServerCommandError
mono_debugger_server_continue             (ServerHandle       *handle);

ServerCommandError
mono_debugger_server_detach               (ServerHandle       *handle);

ServerCommandError
mono_debugger_server_peek_word            (ServerHandle       *handle,
					   guint64             start,
					   guint32            *word);

ServerCommandError
mono_debugger_server_read_memory          (ServerHandle       *handle,
					   guint64             start,
					   guint32             size,
					   gpointer            data);

ServerCommandError
mono_debugger_server_write_memory         (ServerHandle       *handle,
					   guint64             start,
					   guint32             size,
					   gconstpointer       data);

ServerCommandError
mono_debugger_server_call_method          (ServerHandle       *handle,
					   guint64             method_address,
					   guint64             method_argument1,	
					   guint64             method_argument2,
					   guint64             callback_argument);

ServerCommandError
mono_debugger_server_call_method_1        (ServerHandle       *handle,
					   guint64             method_address,
					   guint64             method_argument,
					   const gchar        *string_argument,
					   guint64             callback_argument);

ServerCommandError
mono_debugger_server_call_method_invoke   (ServerHandle       *handle,
					   guint64             invoke_method,
					   guint64             method_argument,
					   guint64             object_argument,
					   guint32             num_params,
					   guint64            *param_data,
					   guint64             callback_argument);

ServerCommandError
mono_debugger_server_insert_breakpoint   (ServerHandle        *handle,
					  guint64              address,
					  guint32             *breakpoint);

ServerCommandError
mono_debugger_server_insert_hw_breakpoint(ServerHandle        *handle,
					  guint32              idx,
					  guint64              address,
					  guint32             *breakpoint);

ServerCommandError
mono_debugger_server_remove_breakpoint   (ServerHandle        *handle,
					  guint32              breakpoint);

ServerCommandError
mono_debugger_server_enable_breakpoint   (ServerHandle        *handle,
					  guint32              breakpoint);

ServerCommandError
mono_debugger_server_disable_breakpoint  (ServerHandle        *handle,
					  guint32              breakpoint);

ServerCommandError
mono_debugger_server_get_registers       (ServerHandle        *handle,
					  guint32              count,
					  guint32             *registers,
					  guint64             *values);

ServerCommandError
mono_debugger_server_set_registers       (ServerHandle        *handle,
					  guint32              count,
					  guint32             *registers,
					  guint64             *values);

ServerCommandError
mono_debugger_server_get_backtrace       (ServerHandle        *handle,
					  gint32               max_frames,
					  guint64              stop_address,
					  guint32             *count,
					  StackFrame         **frames);

ServerCommandError
mono_debugger_server_get_ret_address     (ServerHandle        *handle,
					  guint64             *retval);

ServerCommandError
mono_debugger_server_stop                (ServerHandle        *handle);

ServerCommandError
mono_debugger_server_stop_and_wait       (ServerHandle        *handle,
					  guint32             *status);

ServerCommandError
mono_debugger_server_set_signal          (ServerHandle        *handle,
					  guint32              sig,
					  guint32              send_it);

ServerCommandError
mono_debugger_server_kill                (ServerHandle        *handle);

ServerCommandError
mono_debugger_server_get_signal_info     (ServerHandle        *handle,
					  SignalInfo          *sinfo);


/* POSIX semaphores */

void mono_debugger_server_sem_init (void);
void mono_debugger_server_sem_wait (void);
void mono_debugger_server_sem_post (void);
int mono_debugger_server_sem_get_value (void);

int mono_debugger_server_get_pending_sigint (void);

G_END_DECLS

#endif
