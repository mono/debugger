#ifndef __MONO_DEBUGGER_REMOTING_H__
#define __MONO_DEBUGGER_REMOTING_H__

#include <sys/types.h>
#include <sys/socket.h>
#include <sys/poll.h>
#include <unistd.h>
#include <string.h>
#include <fcntl.h>
#include <errno.h>
#include <signal.h>

#include <glib.h>

G_BEGIN_DECLS

/* C# delegates. */
typedef void (*PollFunc) (void);

gboolean
mono_debugger_remoting_spawn (const gchar **argv, const gchar **envp, gint *child_pid,
			      gint *child_socket, gchar **error);

int
mono_debugger_remoting_setup_server (void);

void
mono_debugger_remoting_kill (int pid, int fd);

int
mono_debugger_remoting_stream_read (int fd, void *buffer, int count);

int
mono_debugger_remoting_stream_write (int fd, const void *buffer, int count);

void
mono_debugger_remoting_poll (int fd, PollFunc func);

G_END_DECLS

#endif
