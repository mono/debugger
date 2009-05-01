#ifndef __MONO_DEBUGGER_SERVER_H__
#define __MONO_DEBUGGER_SERVER_H__

#include <breakpoints.h>
#include <signal.h>
#include <glib.h>

G_BEGIN_DECLS

#define MONO_DEBUGGER_REMOTE_VERSION		1
#define MONO_DEBUGGER_REMOTE_MAGIC		0x36885fe4

/*
 * Keep in sync with TargetExceptionType in classes/TargetException.cs.
 */
typedef enum {
	COMMAND_ERROR_NONE = 0,
	COMMAND_ERROR_UNKNOWN_ERROR,
	COMMAND_ERROR_INTERNAL_ERROR,
	COMMAND_ERROR_NO_TARGET,
	COMMAND_ERROR_ALREADY_HAVE_TARGET,
	COMMAND_ERROR_CANNOT_START_TARGET,
	COMMAND_ERROR_NOT_STOPPED,
	COMMAND_ERROR_ALREADY_STOPPED,
	COMMAND_ERROR_RECURSIVE_CALL,
	COMMAND_ERROR_NO_SUCH_BREAKPOINT,
	COMMAND_ERROR_NO_SUCH_REGISTER,
	COMMAND_ERROR_DR_OCCUPIED,
	COMMAND_ERROR_MEMORY_ACCESS,
	COMMAND_ERROR_NOT_IMPLEMENTED,
	COMMAND_ERROR_IO_ERROR,
	COMMAND_ERROR_NO_CALLBACK_FRAME,
} ServerCommandError;

typedef enum {
	MESSAGE_NONE,
	MESSAGE_UNKNOWN_ERROR = 1,
	MESSAGE_CHILD_EXITED = 2,
	MESSAGE_CHILD_STOPPED,
	MESSAGE_CHILD_SIGNALED,
	MESSAGE_CHILD_CALLBACK,
	MESSAGE_CHILD_CALLBACK_COMPLETED,
	MESSAGE_CHILD_HIT_BREAKPOINT,
	MESSAGE_CHILD_MEMORY_CHANGED,
	MESSAGE_CHILD_CREATED_THREAD,
	MESSAGE_CHILD_FORKED,
	MESSAGE_CHILD_EXECD,
	MESSAGE_CHILD_CALLED_EXIT,
	MESSAGE_CHILD_NOTIFICATION,
	MESSAGE_CHILD_INTERRUPTED,
	MESSAGE_RUNTIME_INVOKE_DONE
} ServerStatusMessageType;

typedef struct {
	ServerStatusMessageType type;
	guint32 arg;
} ServerStatusMessage;

typedef struct {
	guint64 address;
	guint64 stack_pointer;
	guint64 frame_address;
} StackFrame;

#define EXECUTABLE_CODE_CHUNK_SIZE		16

typedef struct
{
	guint32 address_size;
	guint64 notification_address;
	guint64 executable_code_buffer;
	guint32 executable_code_buffer_size;
	guint32 executable_code_chunk_size;
	guint32 executable_code_total_chunks;
	guint64 breakpoint_info_area;
	guint64 breakpoint_table;
	guint32 breakpoint_table_size;

	/* Private */
	guint8 *breakpoint_table_bitfield;
	guint8 *executable_code_bitfield;
} MonoRuntimeInfo;

/* This is an opaque data structure which the backend may use to store stuff. */
typedef struct InferiorVTable InferiorVTable;
typedef struct InferiorHandle InferiorHandle;
typedef struct ServerHandle ServerHandle;
typedef struct ArchInfo ArchInfo;

typedef struct IOThreadData IOThreadData;

/* C# delegates. */
typedef void (*ChildOutputFunc) (gboolean is_stderr, const char *output);

typedef struct {
	int sigkill;
	int sigstop;
	int sigint;
	int sigchld;
	int sigfpe;
	int sigquit;
	int sigabrt;
	int sigsegv;
	int sigill;
	int sigbus;
	int kernel_sigrtmin;
	int mono_thread_abort;
} SignalInfo;

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

struct ServerHandle {
	ArchInfo *arch;
	InferiorHandle *inferior;
	MonoRuntimeInfo *mono_runtime;
	BreakpointManager *bpm;
};

struct InferiorVTable {
	
	void                  (* static_init)         (void);

	void                  (* global_init)         (void);

	ServerHandle *        (* create_inferior)     (BreakpointManager  *bpm);

	ServerCommandError    (* initialize_process)  (ServerHandle       *handle);

	ServerCommandError    (* initialize_thread)   (ServerHandle       *handle,
						       guint32             pid);

	void                  (* set_runtime_info)    (ServerHandle       *handle,
						       MonoRuntimeInfo    *mono_runtime_info);

	void                  (* io_thread_main)      (IOThreadData       *io_data,
						       ChildOutputFunc     func);

	ServerCommandError    (* spawn)               (ServerHandle       *handle,
						       const gchar        *working_directory,
						       const gchar       **argv,
						       const gchar       **envp,
						       gboolean            redirect_fds,
						       gint               *child_pid,
						       IOThreadData      **io_data,
						       gchar             **error);

	ServerCommandError    (* attach)              (ServerHandle       *handle,
						       guint32             pid);

	ServerCommandError    (* detach)              (ServerHandle       *handle);

	void                  (* finalize)            (ServerHandle        *handle);

	guint32               (* global_wait)         (guint32             *status_ret);

	ServerCommandError    (* stop_and_wait)       (ServerHandle        *handle,
						       guint32             *status);

	ServerStatusMessageType (* dispatch_event)    (ServerHandle        *handle,
						       guint32              status,
						       guint64             *arg,
						       guint64             *data1,
						       guint64             *data2,
						       guint32             *opt_data_size,
						       gpointer            *opt_data);

	ServerStatusMessageType (* dispatch_simple)   (guint32              status,
						       guint32             *arg);

	/* Get sizeof (int), sizeof (long) and sizeof (void *) from the target. */
	ServerCommandError    (* get_target_info)     (guint32            *target_int_size,
						       guint32            *target_long_size,
						       guint32            *target_address_size,
						       guint32            *is_bigendian);

	/*
	 * Continue the target.
	 * This operation must start the target and then return immediately
	 * (without waiting for the target to stop).
	 */
	ServerCommandError    (* run)                 (ServerHandle     *handle);

	/*
	 * Single-step one machine instruction.
	 * This operation must start the target and then return immediately
	 * (without waiting for the target to stop).
	 */
	ServerCommandError    (* step)                (ServerHandle     *handle);

	ServerCommandError    (* resume)              (ServerHandle     *handle);

	/*
	 * Get the current program counter.
	 * Return COMMAND_ERROR_NOT_STOPPED if the target is currently running.
	 * This is a time-critical function, it must return immediately without blocking.
	 */
	ServerCommandError    (* get_frame)           (ServerHandle     *handle,
						       StackFrame       *frame);

	/*
	 * Checks whether the current instruction is a breakpoint.
	 */
	ServerCommandError    (* current_insn_is_bpt) (ServerHandle     *handle,
						       guint32          *is_breakpoint);

	ServerCommandError    (* peek_word)           (ServerHandle     *handle,
						       guint64           start,
						       guint64          *word);

	/*
	 * Read `size' bytes from the target's address space starting at `start'.
	 * Writes the result into `buffer' (which has been allocated by the caller).
	 */
	ServerCommandError    (* read_memory)         (ServerHandle     *handle,
						       guint64           start,
						       guint32           size,
						       gpointer          buffer);

	/*
	 * Write `size' bytes from `buffer' to the target's address space starting at `start'.
	 */
	ServerCommandError    (* write_memory)        (ServerHandle     *handle,
						       guint64           start,
						       guint32           size,
						       gconstpointer     data);

	/*
	 * Call `guint64 (*func) (guint64)' function at address `method' in the target address
	 * space, pass it argument `method_argument', send a MESSAGE_CHILD_CALLBACK with the
	 * `callback_argument' and the function's return value when the function returns.
	 * This function must return immediately without waiting for the target !
	 */
	ServerCommandError    (* call_method)         (ServerHandle     *handle,
						       guint64           method,
						       guint64           method_argument1,
						       guint64           method_argument2,
						       guint64           callback_argument);

	/*
	 * Call `guint64 (*func) (guint64, const gchar *)' function at address `method' in the
	 * target address space, pass it arguments `method_argument' and `string_argument' , send
	 * a MESSAGE_CHILD_CALLBACK with the `callback_argument' and the function's return value
	 * when the function returns.
	 * This function must return immediately without waiting for the target !
	 */
	ServerCommandError    (* call_method_1)       (ServerHandle     *handle,
						       guint64           method,
						       guint64           method_argument,
						       guint64           data_argument,
						       guint64           data_argument2,
						       const gchar      *string_argument,
						       guint64           callback_argument);

	ServerCommandError    (* call_method_2)       (ServerHandle     *handle,
						       guint64           method,
						       guint32           data_size,
						       gconstpointer     data_buffer,
						       guint64           callback_argument);

	ServerCommandError    (* call_method_3)       (ServerHandle     *handle,
						       guint64           method_address,
						       guint64           method_argument,
						       guint64           address_argument,
						       guint32           blob_size,
						       gconstpointer     blob_data,
						       guint64           callback_argument);

	ServerCommandError    (* call_method_invoke)  (ServerHandle     *handle,
						       guint64           invoke_method,
						       guint64           method_argument,
						       guint32           num_params,
						       guint32           glob_size,
						       guint64          *param_data,
						       gint32           *offset_data,
						       gconstpointer     blob_data,
						       guint64           callback_argument,
						       gboolean          debug);

	ServerCommandError    (* execute_instruction) (ServerHandle     *handle,
						       const guint8     *instruction,
						       guint32           size,
						       gboolean          update_ip);

	ServerCommandError    (* mark_rti_frame)      (ServerHandle     *handle);

	ServerCommandError    (* abort_invoke)        (ServerHandle     *handle,
						       guint64           stack_pointer,
						       guint64          *aborted_rti);

	/*
	 * Insert a breakpoint at address `address' in the target's address space.
	 * Returns a breakpoint handle in `bhandle' which can be passed to `remove_breakpoint'
	 * to remove the breakpoint.
	 */
	ServerCommandError    (* insert_breakpoint)   (ServerHandle     *handle,
						       guint64           address,
						       guint32          *bhandle);

	/*
	 * Insert a hardware breakpoint at address `address' in the target's address space.
	 * Returns a breakpoint handle in `bhandle' which can be passed to `remove_breakpoint'
	 * to remove the breakpoint.
	 */
	ServerCommandError    (* insert_hw_breakpoint)(ServerHandle     *handle,
						       guint32           type,
						       guint32          *idx,
						       guint64           address,
						       guint32          *bhandle);

	/*
	 * Remove breakpoint `bhandle'.
	 */
	ServerCommandError    (* remove_breakpoint)   (ServerHandle     *handle,
						       guint32           bhandle);

	/*
	 * Enables breakpoint `bhandle'.
	 */
	ServerCommandError    (* enable_breakpoint)   (ServerHandle     *handle,
						       guint32           bhandle);

	/*
	 * Disables breakpoint `bhandle'.
	 */
	ServerCommandError    (* disable_breakpoint)  (ServerHandle     *handle,
						       guint32           bhandle);

	/*
	 * Get all breakpoints.  Writes number of breakpoints into `count' and returns a g_new0()
	 * allocated list of guint32's in `breakpoints'.  The caller is responsible for freeing this
	 * data structure.
	 */
	ServerCommandError    (* get_breakpoints)     (ServerHandle     *handle,
						       guint32          *count,
						       guint32         **breakpoints);

	/*
	 * Get processor registers.
	 *
	 */
	ServerCommandError    (* get_registers)       (ServerHandle     *handle,
						       guint64          *values);

	/*
	 * Set processor registers.
	 *
	 */
	ServerCommandError    (* set_registers)       (ServerHandle     *handle,
						       guint64          *values);

	/*
	 * Stop the target.
	 */
	ServerCommandError    (* stop)                (ServerHandle     *handle);

	/*
	 * Send signal `sig' to the target the next time it is continued.
	 */
	ServerCommandError    (* set_signal)          (ServerHandle     *handle,
						       guint32           sig,
						       guint32           send_it);

	ServerCommandError    (* get_pending_signal)  (ServerHandle     *handle,
						       guint32          *signal);

	/*
	 * Kill the target.
	 */
	ServerCommandError    (* kill)                (ServerHandle     *handle);

	ServerCommandError    (* get_signal_info)     (ServerHandle     *handle,
						       SignalInfo      **sinfo);

	ServerCommandError    (* get_threads)         (ServerHandle     *handle,
						       guint32          *count,
						       guint32         **threads);

	ServerCommandError    (* get_application)     (ServerHandle     *handle,
						       gchar           **exe_file,
						       gchar           **cwd,
						       guint32          *nargs,
						       gchar          ***cmdline_args);

	ServerCommandError    (* detach_after_fork)   (ServerHandle      *handle);

	ServerCommandError    (* push_registers)      (ServerHandle      *handle,
						       guint64           *new_rsp);

	ServerCommandError    (* pop_registers)       (ServerHandle      *handle);

	ServerCommandError    (* get_callback_frame)  (ServerHandle      *handle,
						       guint64            stack_pointer,
						       gboolean           exact_match,
						       guint64           *registers);

	void         (* get_registers_from_core_file) (guint64           *values,
						       const guint8      *buffer);

	guint32               (*get_current_pid) (void);

	guint64               (*get_current_thread) (void);
	
	void                  (*sem_init) (void);

	void                  (*sem_wait) (void);

	void                  (*sem_post) (void);

	int                   (*sem_get_value) (void);

	int                   (*get_pending_sigint) (void);
};

/*
 * Library functions.
 *
 * These functions just call the corresponding function in the ServerHandle's vtable.
 * They're just here to be called from C#.
 */

void
mono_debugger_server_static_init          (void);

void
mono_debugger_server_global_init          (void);

ServerHandle *
mono_debugger_server_create_inferior      (BreakpointManager  *bpm);

ServerCommandError
mono_debugger_server_initialize_process   (ServerHandle       *handle);

ServerCommandError
mono_debugger_server_initialize_thread    (ServerHandle       *handle,
					   guint32             pid);

void
mono_debugger_server_io_thread_main       (IOThreadData       *io_data,
					   ChildOutputFunc     func);

ServerCommandError
mono_debugger_server_spawn                (ServerHandle       *handle,
					   const gchar        *working_directory,
					   const gchar       **argv,
					   const gchar       **envp,
					   gboolean            redirect_fds,
					   gint               *child_pid,
					   IOThreadData      **io_data,
					   gchar             **error);

ServerCommandError
mono_debugger_server_attach               (ServerHandle       *handle,
					   guint32             pid);

void
mono_debugger_server_finalize             (ServerHandle       *handle);

guint32
mono_debugger_server_global_wait          (guint32                 *status);

ServerStatusMessageType
mono_debugger_server_dispatch_event       (ServerHandle            *handle,
					   guint32                  status,
					   guint64                 *arg,
					   guint64                 *data1,
					   guint64                 *data2,
					   guint32                 *opt_data_size,
					   gpointer                *opt_data);

ServerCommandError
mono_debugger_server_get_target_info      (guint32            *target_int_size,
					   guint32            *target_long_size,
					   guint32            *target_address_size,
					   guint32            *is_bigendian);

ServerCommandError
mono_debugger_server_get_frame            (ServerHandle       *handle,
					   StackFrame         *frame);

ServerCommandError
mono_debugger_server_current_insn_is_bpt  (ServerHandle       *handle,
					   guint32            *is_breakpoint);

ServerCommandError
mono_debugger_server_step                 (ServerHandle       *handle);

ServerCommandError
mono_debugger_server_continue             (ServerHandle       *handle);

ServerCommandError
mono_debugger_server_resume               (ServerHandle       *handle);

ServerCommandError
mono_debugger_server_detach               (ServerHandle       *handle);

ServerCommandError
mono_debugger_server_peek_word            (ServerHandle       *handle,
					   guint64             start,
					   guint64            *word);

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
					   guint64             data_argument,
					   guint64             data_argument2,
					   const gchar        *string_argument,
					   guint64             callback_argument);

ServerCommandError
mono_debugger_server_call_method_2        (ServerHandle       *handle,
					   guint64             method_address,
					   guint32             data_size,
					   gconstpointer       data_buffer,
					   guint64             callback_argument);

ServerCommandError
mono_debugger_server_call_method_3        (ServerHandle       *handle,
					   guint64             method_address,
					   guint64             method_argument,
					   guint64             address_argument,
					   guint32             blob_size,
					   gconstpointer       blob_data,
					   guint64             callback_argument);

ServerCommandError
mono_debugger_server_call_method_invoke   (ServerHandle       *handle,
					   guint64             invoke_method,
					   guint64             method_argument,
					   guint32             num_params,
					   guint32             blob_size,
					   guint64            *param_data,
					   gint32             *offset_data,
					   gconstpointer       blob_data,
					   guint64             callback_argument,
					   gboolean            debug);

ServerCommandError
mono_debugger_execute_instruction         (ServerHandle        *handle,
					   const guint8        *instruction,
					   guint32              instruction_size,
					   gboolean             update_ip);

ServerCommandError
mono_debugger_mark_rti_framenvoke        (ServerHandle        *handle);

ServerCommandError
mono_debugger_server_abort_invoke        (ServerHandle        *handle,
					  guint64              stack_pointer,
					  guint64             *aborted_rti);

ServerCommandError
mono_debugger_server_insert_breakpoint   (ServerHandle        *handle,
					  guint64              address,
					  guint32             *breakpoint);

ServerCommandError
mono_debugger_server_insert_hw_breakpoint(ServerHandle        *handle,
					  guint32              type,
					  guint32             *idx,
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
					  guint64             *values);

ServerCommandError
mono_debugger_server_set_registers       (ServerHandle        *handle,
					  guint64             *values);

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
mono_debugger_server_get_pending_signal  (ServerHandle        *handle,
					  guint32             *signal);

ServerCommandError
mono_debugger_server_kill                (ServerHandle        *handle);

ServerCommandError
mono_debugger_server_get_signal_info     (ServerHandle        *handle,
					  SignalInfo         **sinfo);

void
mono_debugger_server_set_runtime_info    (ServerHandle        *handle,
					  MonoRuntimeInfo     *mono_runtime);

ServerCommandError
mono_debugger_server_get_threads         (ServerHandle        *handle,
					  guint32             *count,
					  guint32            **threads);

ServerCommandError
mono_debugger_server_get_application     (ServerHandle        *handle,
					  gchar              **exe_file,
					  gchar              **cwd,
					  guint32             *nargs,
					  gchar             ***cmdline_args);

ServerCommandError
mono_debugger_server_detach_after_fork   (ServerHandle        *handle);

ServerCommandError
mono_debugger_server_push_registers      (ServerHandle        *handle,
					  guint64             *new_rsp);

ServerCommandError
mono_debugger_server_pop_registers       (ServerHandle        *handle);

ServerCommandError
mono_debugger_server_get_callback_frame  (ServerHandle        *handle,
					  guint64              stack_pointer,
					  gboolean             exact_match,
					  guint64             *registers);

MonoRuntimeInfo *
mono_debugger_server_initialize_mono_runtime (guint32 address_size,
					      guint64 notification_address,
					      guint64 executable_code_buffer,
					      guint32 executable_code_buffer_size,
					      guint64 breakpoint_info_area,
					      guint64 breakpoint_table,
					      guint32 breakpoint_table_size);

void
mono_debugger_server_finalize_mono_runtime (MonoRuntimeInfo *runtime);

void
mono_debugger_server_initialize_code_buffer (MonoRuntimeInfo *runtime,
					     guint64 executable_code_buffer,
					     guint32 executable_code_buffer_size);

void
mono_debugger_server_get_registers_from_core_file (guint64 *values,
						   const guint8 *buffer);

guint32
mono_debugger_server_get_current_pid (void);

guint64
mono_debugger_server_get_current_thread (void);


/* POSIX semaphores */

void mono_debugger_server_sem_init (void);
void mono_debugger_server_sem_wait (void);
void mono_debugger_server_sem_post (void);
int mono_debugger_server_sem_get_value (void);

int mono_debugger_server_get_pending_sigint (void);

G_END_DECLS

#endif
