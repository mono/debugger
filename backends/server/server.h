#ifndef __MONO_DEBUGGER_SERVER_H__
#define __MONO_DEBUGGER_SERVER_H__

#include <glib.h>

G_BEGIN_DECLS

#define MONO_SYMBOL_FILE_VERSION		18
#define MONO_SYMBOL_FILE_MAGIC			"45e82623fd7fa614"

typedef enum {
	MESSAGE_CHILD_EXITED = 1,
	MESSAGE_CHILD_STOPPED,
	MESSAGE_CHILD_SIGNALED
} ServerStatusMessageType;

typedef struct {
	ServerStatusMessageType type;
	int arg;
} ServerStatusMessage;

typedef void (*SpawnChildSetupFunc) (void);
typedef void (*SpawnChildExitedFunc) (void);
typedef void (*SpawnChildMessageFunc) (ServerStatusMessageType type, int arg);

extern GQuark mono_debugger_spawn_error_quark (void);

extern gboolean
mono_debugger_spawn_async (const gchar              *working_directory,
			   gchar                   **argv,
			   gchar                   **envp,
			   gboolean                  search_path,
			   SpawnChildSetupFunc       child_setup_cb,
			   gint                     *child_pid,
			   GIOChannel              **status_channel,
			   GIOChannel              **command_channel,
			   SpawnChildExitedFunc      child_exited_cb,
			   SpawnChildMessageFunc     child_message_cb,
			   gint                     *standard_input,
			   gint                     *standard_output,
			   gint                     *standard_error,
			   GError                  **error);

extern gboolean
mono_debugger_process_server_message (GIOChannel              *channel,
				      SpawnChildMessageFunc    child_message_cb);


G_END_DECLS

#endif
