#ifndef __MONO_DEBUGGER_SERVER_H__
#define __MONO_DEBUGGER_SERVER_H__

#include <breakpoints.h>
#include <glib.h>

G_BEGIN_DECLS

typedef enum {
	COMMAND_ERROR_NONE = 0,
	COMMAND_ERROR_UNKNOWN,
	COMMAND_ERROR_NO_INFERIOR,
	COMMAND_ERROR_ALREADY_HAVE_INFERIOR,
	COMMAND_ERROR_FORK,
	COMMAND_ERROR_NOT_STOPPED,
	COMMAND_ERROR_RECURSIVE_CALL,
	COMMAND_ERROR_NO_SUCH_BREAKPOINT,
	COMMAND_ERROR_UNKNOWN_REGISTER,
	COMMAND_ERROR_DR_OCCUPIED,
	COMMAND_ERROR_MEMORY_ACCESS
} ServerCommandError;

typedef enum {
	MESSAGE_CHILD_EXITED = 1,
	MESSAGE_CHILD_STOPPED,
	MESSAGE_CHILD_SIGNALED,
	MESSAGE_CHILD_CALLBACK,
	MESSAGE_CHILD_HIT_BREAKPOINT
} ServerStatusMessageType;

typedef struct {
	ServerStatusMessageType type;
	guint32 arg;
} ServerStatusMessage;

typedef struct {
	guint64 address;
	guint64 params_address;
	guint64 locals_address;
} StackFrame;

/* This is an opaque data structure which the backend may use to store stuff. */
typedef struct InferiorHandle InferiorHandle;

/* C# delegates. */
typedef void (*ChildSetupFunc) (void);
typedef void (*ChildExitedFunc) (void);

/*
 * Server functions.
 *
 * When porting the debugger to another architecture, you need to implement all functions
 * in this vtable.
 *
 * It is a requirement that all functions always return immediately without blocking.
 * If the requested operation cannot be performed (for instance because the target is currently
 * running, don't wait for it but return an error condition!).
 */

typedef struct {
	InferiorHandle *      (* initialize)          (BreakpointManager  *bpm);

	ServerCommandError    (* spawn)               (InferiorHandle     *handle,
						       const gchar        *working_directory,
						       gchar             **argv,
						       gchar             **envp,
						       gboolean            search_path,
						       gint               *child_pid,
						       gint                redirect_fds,
						       gint               *standard_input,
						       gint               *standard_output,
						       gint               *standard_error,
						       gchar             **error);

	ServerCommandError    (* attach)              (InferiorHandle     *handle,
						       int                 pid);

	ServerCommandError    (* detach)              (InferiorHandle     *handle);

	void                  (* finalize)            (InferiorHandle     *handle);

	void                  (* wait)                (InferiorHandle          *handle,
						       ServerStatusMessageType *message,
						       guint64                 *arg,
						       guint64                 *data1,
						       guint64                 *data2);

	/* Get sizeof (int), sizeof (long) and sizeof (void *) from the target. */
	ServerCommandError    (* get_target_info)     (InferiorHandle     *handle,
						       guint32            *target_int_size,
						       guint32            *target_long_size,
						       guint32            *target_address_size);

	/*
	 * Continue the target.
	 * This operation must start the target and then return immediately
	 * (without waiting for the target to stop).
	 */
	ServerCommandError    (* run)                 (InferiorHandle   *handle);

	/*
	 * Single-step one machine instruction.
	 * This operation must start the target and then return immediately
	 * (without waiting for the target to stop).
	 */
	ServerCommandError    (* step)                (InferiorHandle   *handle);

	/*
	 * Get the current program counter.
	 * Return COMMAND_ERROR_NOT_STOPPED if the target is currently running.
	 * This is a time-critical function, it must return immediately without blocking.
	 */
	ServerCommandError    (* get_pc)              (InferiorHandle   *handle,
						       guint64          *pc);

	/*
	 * Checks whether the current instruction is a breakpoint.
	 */
	ServerCommandError    (* current_insn_is_bpt) (InferiorHandle   *handle,
						       guint32          *is_breakpoint);

	/*
	 * Read `size' bytes from the target's address space starting at `start'.
	 * Writes the result into `buffer' (which has been allocated by the caller).
	 */
	ServerCommandError    (* read_data)           (InferiorHandle   *handle,
						       guint64           start,
						       guint32           size,
						       gpointer          buffer);

	/*
	 * Write `size' bytes from `buffer' to the target's address space starting at `start'.
	 */
	ServerCommandError    (* write_data)          (InferiorHandle   *handle,
						       guint64           start,
						       guint32           size,
						       gconstpointer     data);

	/*
	 * Call `guint64 (*func) (guint64)' function at address `method' in the target address
	 * space, pass it argument `method_argument', send a MESSAGE_CHILD_CALLBACK with the
	 * `callback_argument' and the function's return value when the function returns.
	 * This function must return immediately without waiting for the target !
	 */
	ServerCommandError    (* call_method)         (InferiorHandle   *handle,
						       guint64           method,
						       guint64           method_argument,
						       guint64           callback_argument);

	/*
	 * Call `guint64 (*func) (guint64, const gchar *)' function at address `method' in the
	 * target address space, pass it arguments `method_argument' and `string_argument' , send
	 * a MESSAGE_CHILD_CALLBACK with the `callback_argument' and the function's return value
	 * when the function returns.
	 * This function must return immediately without waiting for the target !
	 */
	ServerCommandError    (* call_method_1)       (InferiorHandle   *handle,
						       guint64           method,
						       guint64           method_argument,
						       const gchar      *string_argument,
						       guint64           callback_argument);

	ServerCommandError    (* call_method_invoke)  (InferiorHandle   *handle,
						       guint64           invoke_method,
						       guint64           method_argument,
						       guint64           object_argument,
						       guint32           num_params,
						       guint64          *param_data,
						       guint64           callback_argument);

	/*
	 * Insert a breakpoint at address `address' in the target's address space.
	 * Returns a breakpoint handle in `bhandle' which can be passed to `remove_breakpoint'
	 * to remove the breakpoint.
	 */
	ServerCommandError    (* insert_breakpoint)   (InferiorHandle   *handle,
						       guint64           address,
						       guint32          *bhandle);

	/*
	 * Insert a hardware breakpoint at address `address' in the target's address space.
	 * Returns a breakpoint handle in `bhandle' which can be passed to `remove_breakpoint'
	 * to remove the breakpoint.
	 */
	ServerCommandError    (* insert_hw_breakpoint)(InferiorHandle   *handle,
						       guint32           idx,
						       guint64           address,
						       guint32          *bhandle);

	/*
	 * Remove breakpoint `bhandle'.
	 */
	ServerCommandError    (* remove_breakpoint)   (InferiorHandle   *handle,
						       guint32           bhandle);

	/*
	 * Enables breakpoint `bhandle'.
	 */
	ServerCommandError    (* enable_breakpoint)   (InferiorHandle   *handle,
						       guint32           bhandle);

	/*
	 * Disables breakpoint `bhandle'.
	 */
	ServerCommandError    (* disable_breakpoint)  (InferiorHandle   *handle,
						       guint32           bhandle);

	/*
	 * Get all breakpoints.  Writes number of breakpoints into `count' and returns a g_new0()
	 * allocated list of guint32's in `breakpoints'.  The caller is responsible for freeing this
	 * data structure.
	 */
	ServerCommandError    (* get_breakpoints)     (InferiorHandle   *handle,
						       guint32          *count,
						       guint32         **breakpoints);

	/*
	 * Get processor registers.
	 *
	 */
	ServerCommandError    (* get_registers)       (InferiorHandle   *handle,
						       guint32           count,
						       guint32          *registers,
						       guint64          *values);

	/*
	 * Set processor registers.
	 *
	 */
	ServerCommandError    (* set_registers)       (InferiorHandle   *handle,
						       guint32           count,
						       guint32          *registers,
						       guint64          *values);

	/*
	 * Get backtrace.  This tries to return a partial backtrace if possible, so check the `count'
	 * and `frames' values even on an error.
	 */
	ServerCommandError    (* get_backtrace)       (InferiorHandle   *handle,
						       gint32            max_frames,
						       guint64           stop_address,
						       guint32          *count,
						       StackFrame       **frames);

	/*
	 * This is only allowed on the first instruction of a method.
	 */
	ServerCommandError    (* get_ret_address)     (InferiorHandle   *handle,
						       guint64          *retval);

	/*
	 * Stop the target.
	 */
	ServerCommandError    (* stop)                (InferiorHandle   *handle);

	/*
	 * Send signal `sig' to the target the next time it is continued.
	 */
	ServerCommandError    (* set_signal)          (InferiorHandle   *handle,
						       guint32           sig,
						       guint32           send_it);

	/*
	 * Kill the target.
	 */
	ServerCommandError    (* kill)                (InferiorHandle   *handle);

} InferiorInfo;

/*
 * Library functions.
 *
 * These functions just call the corresponding function in the ServerHandle's vtable.
 * They're just here to be called from C#.
 */

typedef struct {
	int has_inferior;
	InferiorHandle *inferior;
	InferiorInfo *info;
} ServerHandle;

ServerHandle *
mono_debugger_server_initialize           (BreakpointManager  *bpm);

ServerCommandError
mono_debugger_server_spawn                (ServerHandle       *handle,
					   const gchar        *working_directory,
					   gchar             **argv,
					   gchar             **envp,
					   gboolean            search_path,
					   gint               *child_pid,
					   gint                redirect_fds,
					   gint               *standard_input,
					   gint               *standard_output,
					   gint               *standard_error,
					   gchar             **error);

ServerCommandError
mono_debugger_server_attach               (ServerHandle       *handle,
					   int                 pid);

void
mono_debugger_server_finalize             (ServerHandle       *handle);

void
mono_debugger_server_wait                 (ServerHandle            *handle,
					   ServerStatusMessageType *message,
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
mono_debugger_server_read_memory          (ServerHandle       *handle,
					   guint64             start,
					   guint32             size,
					   gpointer           *data);

ServerCommandError
mono_debugger_server_write_memory         (ServerHandle       *handle,
					   gpointer            data,
					   guint64             start,
					   guint32             size);

ServerCommandError
mono_debugger_server_call_method          (ServerHandle       *handle,
					   guint64             method_address,
					   guint64             method_argument,
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
mono_debugger_server_stop                (ServerHandle       *handle);

ServerCommandError
mono_debugger_server_set_signal          (ServerHandle       *handle,
					  guint32             sig,
					  guint32             send_it);

ServerCommandError
mono_debugger_server_kill                (ServerHandle       *handle);

/* Signals. */
int mono_debugger_server_get_sigkill                  (void);
int mono_debugger_server_get_sigstop                  (void);
int mono_debugger_server_get_sigint                   (void);
int mono_debugger_server_get_sigchld                  (void);
int mono_debugger_server_get_sigprof                  (void);
int mono_debugger_server_get_sigpwr                   (void);
int mono_debugger_server_get_sigxcpu                  (void);
int mono_debugger_server_get_thread_abort_signal      (void);
int mono_debugger_server_get_thread_restart_signal    (void);
int mono_debugger_server_get_thread_debug_signal      (void);
int mono_debugger_server_get_mono_thread_debug_signal (void);


G_END_DECLS

#endif
