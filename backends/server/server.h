#ifndef __MONO_DEBUGGER_SERVER_H__
#define __MONO_DEBUGGER_SERVER_H__

#include <glib.h>

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
	SERVER_COMMAND_GET_TARGET_INFO
} ServerCommand;

typedef enum {
	COMMAND_ERROR_NONE = 0,
	COMMAND_ERROR_IO,
	COMMAND_ERROR_UNKNOWN,
	COMMAND_ERROR_INVALID_COMMAND,
	COMMAND_ERROR_NOT_STOPPED
} ServerCommandError;

typedef enum {
	MESSAGE_CHILD_EXITED = 1,
	MESSAGE_CHILD_STOPPED,
	MESSAGE_CHILD_SIGNALED
} ServerStatusMessageType;

typedef struct {
	ServerStatusMessageType type;
	int arg;
} ServerStatusMessage;

typedef struct InferiorHandle InferiorHandle;

typedef void (*SpawnChildSetupFunc) (void);
typedef void (*SpawnChildExitedFunc) (void);
typedef void (*SpawnChildMessageFunc) (ServerStatusMessageType type, int arg);

typedef struct {
	GIOChannel *status_channel;
	SpawnChildMessageFunc child_message_cb;
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
			   gint                     *standard_input,
			   gint                     *standard_output,
			   gint                     *standard_error,
			   GError                  **error);

/*
 * Server functions.
 */

extern InferiorHandle *
server_ptrace_get_handle             (int                      pid);

extern void
server_ptrace_traceme                (int                      pid);

extern InferiorHandle *
server_ptrace_attach                 (int                      pid);

extern ServerCommandError
server_ptrace_continue               (InferiorHandle          *handle);

extern ServerCommandError
server_ptrace_step                   (InferiorHandle          *handle);

extern ServerCommandError
server_ptrace_detach                 (InferiorHandle          *handle);

extern ServerCommandError
server_get_program_counter           (InferiorHandle          *handle,
				      guint64                 *pc);

extern ServerCommandError
server_ptrace_read_data              (InferiorHandle          *handle,
				      guint64                  start,
				      guint32                  size,
				      gpointer                 buffer);

/*
 * Library functions.
 */

extern gboolean
mono_debugger_process_server_message (ServerHandle            *handle);

extern ServerCommandError
mono_debugger_server_send_command    (ServerHandle            *handle,
				      ServerCommand            command);

extern ServerCommandError
mono_debugger_server_read_memory     (ServerHandle            *handle,
				      guint64                  start,
				      guint32                  size,
				      gpointer                *data);

extern ServerCommandError
mono_debugger_server_get_target_info (ServerHandle            *handle,
				      guint32                 *target_int_size,
				      guint32                 *target_long_size,
				      guint32                 *target_address_size);

extern gboolean
mono_debugger_server_read_uint64     (ServerHandle            *handle,
				      guint64                 *arg);

extern gboolean
mono_debugger_server_write_uint64    (ServerHandle            *handle,
				      guint64                  arg);

G_END_DECLS

#endif
