#ifndef __MONO_DEBUGGER_SERVER_H__
#define __MONO_DEBUGGER_SERVER_H__

#include <glib.h>

G_BEGIN_DECLS

#define MONO_SYMBOL_FILE_VERSION		18
#define MONO_SYMBOL_FILE_MAGIC			"45e82623fd7fa614"

typedef enum {
	COMMAND_ERROR_NONE = 0,
	COMMAND_ERROR_NO_INFERIOR,
	COMMAND_ERROR_ALREADY_HAVE_INFERIOR,
	COMMAND_ERROR_FORK,
	COMMAND_ERROR_IO,
	COMMAND_ERROR_UNKNOWN,
	COMMAND_ERROR_INVALID_COMMAND,
	COMMAND_ERROR_NOT_STOPPED,
	COMMAND_ERROR_ALIGNMENT,
	COMMAND_ERROR_RECURSIVE_CALL,
	COMMAND_ERROR_NO_SUCH_BREAKPOINT,
	COMMAND_ERROR_UNKNOWN_REGISTER
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

/* This is an opaque data structure which the backend may use to store stuff. */
typedef struct InferiorHandle InferiorHandle;

/* C# delegates. */
typedef void (*ChildSetupFunc) (void);
typedef void (*ChildExitedFunc) (void);
typedef void (*ChildMessageFunc) (ServerStatusMessageType type, int arg);
typedef void (*ChildCallbackFunc) (guint64 callback, guint64 data);

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
	InferiorHandle *      (* spawn)               (const gchar        *working_directory,
						       gchar             **argv,
						       gchar             **envp,
						       gboolean            search_path,
						       ChildExitedFunc     child_exited,
						       ChildMessageFunc    child_message,
						       ChildCallbackFunc   child_callback,
						       gint               *child_pid,
						       gint               *standard_input,
						       gint               *standard_output,
						       gint               *standard_error,
						       GError            **error);

	InferiorHandle *      (* attach)              (int                 pid,
						       ChildExitedFunc     child_exited,
						       ChildMessageFunc    child_message,
						       ChildCallbackFunc   child_callback);

	ServerCommandError    (* detach)              (InferiorHandle     *handle);

	void                  (* finalize)            (InferiorHandle     *handle);

	/* These two are private, will provide docu soon. */
	GSource *             (* get_g_source)        (InferiorHandle     *handle);
	void                  (* wait)                (InferiorHandle     *handle);

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
	 * Insert a breakpoint at address `address' in the target's address space.
	 * Returns a breakpoint handle in `bhandle' which can be passed to `remove_breakpoint'
	 * to remove the breakpoint.
	 */
	ServerCommandError    (* insert_breakpoint)   (InferiorHandle   *handle,
						       guint64           address,
						       guint32          *bhandle);

	/*
	 * Remove breakpoint `bhandle'.
	 */
	ServerCommandError    (* remove_breakpoint)   (InferiorHandle   *handle,
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
} InferiorInfo;

extern InferiorInfo i386_linux_ptrace_inferior;

/*
 * Library functions.
 *
 * These functions just call the corresponding function in the ServerHandle's vtable.
 * They're just here to be called from C#.
 */

typedef struct {
	ChildMessageFunc child_message_cb;
	ChildCallbackFunc child_callback_cb;
	InferiorHandle *inferior;
	InferiorInfo *info;
	int fd, pid;
} ServerHandle;

ServerHandle *
mono_debugger_server_initialize           (void);

ServerCommandError
mono_debugger_server_spawn                (ServerHandle       *handle,
					   const gchar        *working_directory,
					   gchar             **argv,
					   gchar             **envp,
					   gboolean            search_path,
					   ChildExitedFunc     child_exited,
					   ChildMessageFunc    child_message,
					   ChildCallbackFunc   child_callback,
					   gint               *child_pid,
					   gint               *standard_input,
					   gint               *standard_output,
					   gint               *standard_error,
					   GError            **error);

ServerCommandError
mono_debugger_server_attach               (ServerHandle       *handle,
					   int                 pid,
					   ChildExitedFunc     child_exited,
					   ChildMessageFunc    child_message,
					   ChildCallbackFunc   child_callback);

GSource *
mono_debugger_server_get_g_source         (ServerHandle       *handle);

void
mono_debugger_server_finalize             (ServerHandle       *handle);

void
mono_debugger_server_wait                 (ServerHandle       *handle);

ServerCommandError
mono_debugger_server_get_target_info      (ServerHandle       *handle,
					   guint32            *target_int_size,
					   guint32            *target_long_size,
					   guint32            *target_address_size);

ServerCommandError
mono_debugger_server_get_pc               (ServerHandle       *handle,
					   guint64            *pc);

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
mono_debugger_server_insert_breakpoint   (ServerHandle        *handle,
					  guint64              address,
					  guint32             *breakpoint);

ServerCommandError
mono_debugger_server_remove_breakpoint   (ServerHandle        *handle,
					  guint32              breakpoint);

ServerCommandError
mono_debugger_server_get_registers       (ServerHandle        *handle,
					  guint32              count,
					  guint32             *registers,
					  guint64             *values);

G_END_DECLS

#endif
