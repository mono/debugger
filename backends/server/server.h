#ifndef __MONO_DEBUGGER_SERVER_H__
#define __MONO_DEBUGGER_SERVER_H__

#include <glib.h>
#include <util.h>

G_BEGIN_DECLS

#define MONO_SYMBOL_FILE_VERSION		18
#define MONO_SYMBOL_FILE_MAGIC			"45e82623fd7fa614"

typedef enum {
	SERVER_COMMAND_GET_PC = 1,
	SERVER_COMMAND_DETACH,
	SERVER_COMMAND_SHUTDOWN,
	SERVER_COMMAND_KILL,
	SERVER_COMMAND_CONTINUE,
	SERVER_COMMAND_STEP,
	SERVER_COMMAND_READ_DATA,
	SERVER_COMMAND_WRITE_DATA,
	SERVER_COMMAND_GET_TARGET_INFO,
	SERVER_COMMAND_CALL_METHOD,
	SERVER_COMMAND_INSERT_BREAKPOINT,
	SERVER_COMMAND_REMOVE_BREAKPOINT
} ServerCommand;

typedef enum {
	COMMAND_ERROR_NONE = 0,
	COMMAND_ERROR_IO,
	COMMAND_ERROR_UNKNOWN,
	COMMAND_ERROR_INVALID_COMMAND,
	COMMAND_ERROR_NOT_STOPPED,
	COMMAND_ERROR_ALIGNMENT,
	COMMAND_ERROR_RECURSIVE_CALL,
	COMMAND_ERROR_NO_SUCH_BREAKPOINT
} ServerCommandError;

typedef enum {
	MESSAGE_CHILD_EXITED = 1,
	MESSAGE_CHILD_STOPPED,
	MESSAGE_CHILD_SIGNALED,
	MESSAGE_CHILD_CALLBACK,
	MESSAGE_CHILD_HIT_BREAKPOINT
} ServerStatusMessageType;

typedef enum {
	STOP_ACTION_SEND_STOPPED,
	STOP_ACTION_BREAKPOINT_HIT,
	STOP_ACTION_CALLBACK
} ChildStoppedAction;

typedef struct {
	ServerStatusMessageType type;
	guint32 arg;
} ServerStatusMessage;

typedef struct InferiorHandle InferiorHandle;

typedef void (*SpawnChildSetupFunc) (void);
typedef void (*SpawnChildExitedFunc) (void);
typedef void (*SpawnChildMessageFunc) (ServerStatusMessageType type, int arg);
typedef void (*SpawnChildCallbackFunc) (guint64 callback, guint64 data);

typedef struct {
	GIOChannel *status_channel;
	SpawnChildMessageFunc child_message_cb;
	SpawnChildCallbackFunc child_callback_cb;
	int fd, pid;
} ServerHandle;

extern GQuark mono_debugger_spawn_error_quark (void);

extern gboolean
mono_debugger_spawn_async (const gchar              *working_directory,
			   gchar                   **argv,
			   gchar                   **envp,
			   gboolean                  search_path,
			   SpawnChildSetupFunc       child_setup_cb,
			   gint                     *child_pid,
			   GIOChannel              **status_channel,
			   ServerHandle            **server_handle,
			   SpawnChildExitedFunc      child_exited_cb,
			   SpawnChildMessageFunc     child_message_cb,
			   SpawnChildCallbackFunc    child_callback_cb,
			   gint                     *standard_input,
			   gint                     *standard_output,
			   gint                     *standard_error,
			   GError                  **error);

/*
 * Server functions.
 */

typedef struct {
	InferiorHandle *      (* initialize)          (int               pid);

	InferiorHandle *      (* attach)              (int               pid);

	void                  (* traceme)             (int               pid);

	ServerCommandError    (* detach)              (InferiorHandle   *handle);

	ServerCommandError    (* run)                 (InferiorHandle   *handle);

	ServerCommandError    (* step)                (InferiorHandle   *handle);

	ServerCommandError    (* get_pc)              (InferiorHandle   *handle,
						       guint64          *pc);

	ServerCommandError    (* read_data)           (InferiorHandle   *handle,
						       guint64           start,
						       guint32           size,
						       gpointer          buffer);

	ServerCommandError    (* write_data)          (InferiorHandle   *handle,
						       guint64           start,
						       guint32           size,
						       gconstpointer     data);

	ServerCommandError    (* call_method)         (InferiorHandle   *handle,
						       guint64           method,
						       guint64           method_argument,
						       guint64           callback_argument);

	ChildStoppedAction    (* child_stopped)       (InferiorHandle   *handle,
						       int               signumber,
						       guint64          *callback_arg,
						       guint64          *retval);

	ServerCommandError    (* insert_breakpoint)   (InferiorHandle   *handle,
						       guint64           address,
						       guint32          *bhandle);
	ServerCommandError    (* remove_breakpoint)   (InferiorHandle   *handle,
						       guint32           bhandle);
	ServerCommandError    (* get_breakpoints)     (InferiorHandle   *handle,
						       guint32          *count,
						       guint32         **breakpoints);
} InferiorInfo;

extern InferiorInfo i386_linux_ptrace_inferior;

/*
 * Library functions.
 */

extern gboolean
mono_debugger_process_server_message      (ServerHandle            *handle);

extern ServerCommandError
mono_debugger_server_send_command         (ServerHandle            *handle,
					   ServerCommand            command);

extern ServerCommandError
mono_debugger_server_read_memory          (ServerHandle            *handle,
					   guint64                  start,
					   guint32                  size,
					   gpointer                *data);

extern ServerCommandError
mono_debugger_server_write_memory         (ServerHandle            *handle,
					   gpointer                 data,
					   guint64                  start,
					   guint32                  size);

extern ServerCommandError
mono_debugger_server_get_target_info      (ServerHandle            *handle,
					   guint32                 *target_int_size,
					   guint32                 *target_long_size,
					   guint32                 *target_address_size);

extern ServerCommandError
mono_debugger_server_call_method          (ServerHandle            *handle,
					   guint64                  method_address,
					   guint64                  method_argument,
					   guint64                  callback_argument);

extern ServerCommandError
mono_debugger_server_insert_breakpoint   (ServerHandle            *handle,
					  guint64                  address,
					  guint32                 *breakpoint);

extern ServerCommandError
mono_debugger_server_remove_breakpoint   (ServerHandle            *handle,
					  guint32                  breakpoint);

extern gboolean
mono_debugger_server_read_uint64          (ServerHandle            *handle,
					   guint64                 *arg);

extern gboolean
mono_debugger_server_write_uint64         (ServerHandle            *handle,
					   guint64                  arg);

extern gboolean
mono_debugger_server_read_uint32          (ServerHandle            *handle,
					   guint32                 *arg);

extern gboolean
mono_debugger_server_write_uint32         (ServerHandle            *handle,
					   guint32                  arg);

G_END_DECLS

#endif
