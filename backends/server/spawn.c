/* -*- Mode: C; c-basic-offset: 2 -*- */

/*
 * This is a modified version of g_spawn_async_with_pipes() from glib 2.0.6.
 */

/* gspawn.c - Process launching
 *
 *  Copyright 2000 Red Hat, Inc.
 *  g_execvpe implementation based on GNU libc execvp:
 *   Copyright 1991, 92, 95, 96, 97, 98, 99 Free Software Foundation, Inc.
 *
 * GLib is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public License as
 * published by the Free Software Foundation; either version 2 of the
 * License, or (at your option) any later version.
 *
 * GLib is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with GLib; see the file COPYING.LIB.  If not, write
 * to the Free Software Foundation, Inc., 59 Temple Place - Suite 330,
 * Boston, MA 02111-1307, USA.
 */

#include <config.h>

#include "glib.h"
#include <sys/time.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <unistd.h>
#include <errno.h>
#include <fcntl.h>
#include <signal.h>
#include <string.h>

#include <server.h>

#ifdef HAVE_SYS_SELECT_H
#include <sys/select.h>
#endif /* HAVE_SYS_SELECT_H */

#define _(String) (String)
#define N_(String) (String)
#define textdomain(String) (String)
#define gettext(String) (String)
#define dgettext(Domain,String) (String)
#define dcgettext(Domain,String,Type) (String)
#define bindtextdomain(Domain,Directory) (Domain) 

static gint g_execute (const gchar  *file,
                       gchar **argv,
                       gchar **envp,
                       gboolean search_path);

static gboolean make_pipe            (gint                   p[2],
                                      GError                **error);
static gboolean make_socketpair      (gint                    p[2],
                                      GError                **error);
static gboolean fork_exec_with_pipes (const gchar            *working_directory,
                                      gchar                 **argv,
                                      gchar                 **envp,
                                      gboolean                search_path,
				      SpawnChildSetupFunc     child_setup_cb,
                                      gint                   *child_pid,
				      gint                   *status_fd,
				      gint                   *command_fd,
                                      gint                   *standard_input,
                                      gint                   *standard_output,
                                      gint                   *standard_error,
                                      GError                **error);

GQuark
mono_debugger_spawn_error_quark (void)
{
  static GQuark quark = 0;
  if (quark == 0)
    quark = g_quark_from_static_string ("mono-debugger-exec-error-quark");
  return quark;
}

/* Avoids a danger in threaded situations (calling close()
 * on a file descriptor twice, and another thread has
 * re-opened it since the first close)
 */
static gint
close_and_invalidate (gint *fd)
{
  gint ret;

  if (*fd < 0)
    return -1;
  else
    {
      ret = close (*fd);
      *fd = -1;
    }

  return ret;
}

typedef enum
{
  READ_FAILED = 0, /* FALSE */
  READ_OK,
  READ_EOF
} ReadResult;

static gboolean
watch_input_func (GIOChannel *channel, GIOCondition condition, gpointer data)
{
  if (condition != G_IO_IN)
    return TRUE;

  return mono_debugger_process_server_message (data);
}

static gboolean
watch_hangup_func (GIOChannel *channel, GIOCondition condition, gpointer data)
{
  if (condition != G_IO_HUP)
    return TRUE;

  ((SpawnChildExitedFunc) data) ();

  return FALSE;
}

/**
 * mono_debugger_spawn_async:
 *
 * This is heavily based on g_spawn_aync_with_pipes().
 * 
 * Return value: %TRUE on success, %FALSE if an error was set
 **/
gboolean
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
			   GError                  **error)
{
  gboolean retval;
  GIOFlags flags;
  gint status_fd, command_fd;

  g_return_val_if_fail (argv != NULL, FALSE);
  g_return_val_if_fail (status_channel != NULL, FALSE);
  g_return_val_if_fail (server_handle != NULL, FALSE);
  g_return_val_if_fail (standard_output != NULL, FALSE);
  g_return_val_if_fail (standard_error != NULL, FALSE);
  g_return_val_if_fail (standard_input != NULL, FALSE);

  retval = fork_exec_with_pipes (working_directory,
				 argv,
				 envp,
				 search_path,
				 child_setup_cb,
				 child_pid,
				 &status_fd,
				 &command_fd,
				 standard_input,
				 standard_output,
				 standard_error,
				 error);

  if (!retval)
    {
      g_warning (G_STRLOC ": Can't spawn: %s", (*error)->message);
      return FALSE;
    }

  g_message (G_STRLOC ": %d - %d - %d", *child_pid, status_fd, command_fd);

  *status_channel = g_io_channel_unix_new (status_fd);
  flags = g_io_channel_get_flags (*status_channel);
  g_io_channel_set_flags (*status_channel, flags | G_IO_FLAG_NONBLOCK, NULL);

  if (g_io_channel_set_encoding (*status_channel, NULL, error) != G_IO_STATUS_NORMAL)
    {
      g_warning (G_STRLOC ": Can't set encoding on status channel: %s", (*error)->message);
      return 1;
    }

  g_io_channel_set_buffered (*status_channel, FALSE);

  *server_handle = g_new0 (ServerHandle, 1);
  (*server_handle)->status_channel = *status_channel;
  (*server_handle)->child_message_cb = child_message_cb;
  (*server_handle)->fd = command_fd;
  (*server_handle)->pid = *child_pid;

  if (child_message_cb)
    g_io_add_watch (*status_channel, G_IO_IN, watch_input_func, *server_handle);
  if (child_exited_cb)
    g_io_add_watch (*status_channel, G_IO_HUP, watch_hangup_func, child_exited_cb);

  return TRUE;
}

static gint
exec_err_to_g_error (gint en)
{
  switch (en)
    {
#ifdef EACCES
    case EACCES:
      return G_SPAWN_ERROR_ACCES;
      break;
#endif

#ifdef EPERM
    case EPERM:
      return G_SPAWN_ERROR_PERM;
      break;
#endif

#ifdef E2BIG
    case E2BIG:
      return G_SPAWN_ERROR_2BIG;
      break;
#endif

#ifdef ENOEXEC
    case ENOEXEC:
      return G_SPAWN_ERROR_NOEXEC;
      break;
#endif

#ifdef ENAMETOOLONG
    case ENAMETOOLONG:
      return G_SPAWN_ERROR_NAMETOOLONG;
      break;
#endif

#ifdef ENOENT
    case ENOENT:
      return G_SPAWN_ERROR_NOENT;
      break;
#endif

#ifdef ENOMEM
    case ENOMEM:
      return G_SPAWN_ERROR_NOMEM;
      break;
#endif

#ifdef ENOTDIR
    case ENOTDIR:
      return G_SPAWN_ERROR_NOTDIR;
      break;
#endif

#ifdef ELOOP
    case ELOOP:
      return G_SPAWN_ERROR_LOOP;
      break;
#endif
      
#ifdef ETXTBUSY
    case ETXTBUSY:
      return G_SPAWN_ERROR_TXTBUSY;
      break;
#endif

#ifdef EIO
    case EIO:
      return G_SPAWN_ERROR_IO;
      break;
#endif

#ifdef ENFILE
    case ENFILE:
      return G_SPAWN_ERROR_NFILE;
      break;
#endif

#ifdef EMFILE
    case EMFILE:
      return G_SPAWN_ERROR_MFILE;
      break;
#endif

#ifdef EINVAL
    case EINVAL:
      return G_SPAWN_ERROR_INVAL;
      break;
#endif

#ifdef EISDIR
    case EISDIR:
      return G_SPAWN_ERROR_ISDIR;
      break;
#endif

#ifdef ELIBBAD
    case ELIBBAD:
      return G_SPAWN_ERROR_LIBBAD;
      break;
#endif
      
    default:
      return G_SPAWN_ERROR_FAILED;
      break;
    }
}

static void
write_err_and_exit (gint fd, gint msg)
{
  gint en = errno;
  
  write (fd, &msg, sizeof(msg));
  write (fd, &en, sizeof(en));
  
  _exit (1);
}

static void
set_cloexec (gint fd)
{
  fcntl (fd, F_SETFD, FD_CLOEXEC);
}

static gint
sane_dup2 (gint fd1, gint fd2)
{
  gint ret;

 retry:
  ret = dup2 (fd1, fd2);
  if (ret < 0 && errno == EINTR)
    goto retry;

  return ret;
}

enum
{
  CHILD_CHDIR_FAILED,
  CHILD_EXEC_FAILED,
  CHILD_DUP2_FAILED,
  CHILD_FORK_FAILED
};

static void
do_exec (gint                 child_err_report_fd,
         gint                 stdin_fd,
         gint                 stdout_fd,
         gint                 stderr_fd,
	 gint                 status_fd,
	 gint                 command_fd,
         const gchar         *working_directory,
         gchar              **argv,
         gchar              **envp,
         gboolean             search_path,
	 SpawnChildSetupFunc  child_setup_cb)
{
  sigset_t mask;
  gint open_max;
  gint i;
      
  if (working_directory && chdir (working_directory) < 0)
    write_err_and_exit (child_err_report_fd,
                        CHILD_CHDIR_FAILED);

  /* Close all file descriptors but stdin stdout and stderr as
   * soon as we exec. Note that this includes
   * child_err_report_fd, which keeps the parent from blocking
   * forever on the other end of that pipe.
   */
  open_max = sysconf (_SC_OPEN_MAX);
  for (i = 3; i < open_max; i++)
    set_cloexec (i);
  
  /* Redirect pipes as required */

  /* dup2 can't actually fail here I don't think */
  if (sane_dup2 (stdin_fd, 0) < 0)
    write_err_and_exit (child_err_report_fd,
			CHILD_DUP2_FAILED);

  /* ignore this if it doesn't work */
  close_and_invalidate (&stdin_fd);

  /* dup2 can't actually fail here I don't think */
  if (sane_dup2 (stdout_fd, 1) < 0)
    write_err_and_exit (child_err_report_fd,
			CHILD_DUP2_FAILED);

  /* ignore this if it doesn't work */
  close_and_invalidate (&stdout_fd);

  /* dup2 can't actually fail here I don't think */
  if (sane_dup2 (stderr_fd, 2) < 0)
    write_err_and_exit (child_err_report_fd,
			CHILD_DUP2_FAILED);

  /* ignore this if it doesn't work */
  close_and_invalidate (&stderr_fd);
  
  /* dup2 can't actually fail here I don't think */
  if (sane_dup2 (status_fd, 3) < 0)
    write_err_and_exit (child_err_report_fd,
			CHILD_DUP2_FAILED);

  /* ignore this if it doesn't work */
  close_and_invalidate (&status_fd);
  
  /* dup2 can't actually fail here I don't think */
  if (sane_dup2 (command_fd, 4) < 0)
    write_err_and_exit (child_err_report_fd,
			CHILD_DUP2_FAILED);

  /* ignore this if it doesn't work */
  close_and_invalidate (&command_fd);
  
  /* Call user function just before we exec */
  if (child_setup_cb)
    {
      (* child_setup_cb) ();
    }

  /* Since we're sending SIGUSR1's to the server, we must block it here
   * to avoid a race condition where the server gets the signal before it
   * can install its handler. */

  sigemptyset (&mask);
  sigaddset (&mask, SIGUSR1);
  sigaddset (&mask, SIGCHLD);
  sigprocmask (SIG_BLOCK, &mask, NULL);

  g_execute (argv[0], argv, envp, search_path);

  /* Exec failed */
  write_err_and_exit (child_err_report_fd,
                      CHILD_EXEC_FAILED);
}

static gboolean
read_ints (int      fd,
           gint*    buf,
           gint     n_ints_in_buf,    
           gint    *n_ints_read,      
           GError **error)
{
  gsize bytes = 0;    
  
  while (TRUE)
    {
      gssize chunk;    

      if (bytes >= sizeof(gint)*2)
        break; /* give up, who knows what happened, should not be
                * possible.
                */
          
    again:
      chunk = read (fd,
                    ((gchar*)buf) + bytes,
                    sizeof(gint) * n_ints_in_buf - bytes);
      if (chunk < 0 && errno == EINTR)
        goto again;
          
      if (chunk < 0)
        {
          /* Some weird shit happened, bail out */
              
          g_set_error (error,
                       G_SPAWN_ERROR,
                       G_SPAWN_ERROR_FAILED,
                       _("Failed to read from child pipe (%s)"),
                       g_strerror (errno));

          return FALSE;
        }
      else if (chunk == 0)
        break; /* EOF */
      else /* chunk > 0 */
	bytes += chunk;
    }

  *n_ints_read = (gint)(bytes / sizeof(gint));

  return TRUE;
}

static gboolean
fork_exec_with_pipes (const gchar            *working_directory,
		      gchar                 **argv,
		      gchar                 **envp,
		      gboolean                search_path,
		      SpawnChildSetupFunc     child_setup_cb,
		      gint                   *child_pid,
		      gint                   *status_fd,
		      gint                   *command_fd,
		      gint                   *standard_input,
		      gint                   *standard_output,
		      gint                   *standard_error,
		      GError                **error)
{
  gint pid;
  gint stdin_pipe[2] = { -1, -1 };
  gint stdout_pipe[2] = { -1, -1 };
  gint stderr_pipe[2] = { -1, -1 };
  gint child_err_report_pipe[2] = { -1, -1 };
  gint command_socket[2] = { -1, -1 };
  gint status_pipe[2] = { -1, -1 };
  
  if (!make_pipe (child_err_report_pipe, error))
    return FALSE;

  if (!make_pipe (stdin_pipe, error))
    goto cleanup_and_fail;
  
  if (!make_pipe (stdout_pipe, error))
    goto cleanup_and_fail;

  if (!make_pipe (stderr_pipe, error))
    goto cleanup_and_fail;

  if (!make_socketpair (command_socket, error))
    goto cleanup_and_fail;

  if (!make_pipe (status_pipe, error))
    goto cleanup_and_fail;

  pid = fork ();

  if (pid < 0)
    {      
      g_set_error (error,
                   G_SPAWN_ERROR,
                   G_SPAWN_ERROR_FORK,
                   _("Failed to fork (%s)"),
                   g_strerror (errno));

      goto cleanup_and_fail;
    }
  else if (pid == 0)
    {
      /* Be sure we crash if the parent exits
       * and we write to the err_report_pipe
       */
      signal (SIGPIPE, SIG_DFL);

      do_exec (child_err_report_pipe[1],
	       stdin_pipe[0],
	       stdout_pipe[1],
	       stderr_pipe[1],
	       status_pipe[1],
	       command_socket[0],
	       working_directory,
	       argv,
	       envp,
	       search_path,
	       child_setup_cb);
    }
  else
    {
      /* Parent */
      
      gint buf[2];
      gint n_ints = 0;    

      /* Close the uncared-about ends of the pipes */
      close_and_invalidate (&child_err_report_pipe[1]);
      close_and_invalidate (&stdin_pipe[0]);
      close_and_invalidate (&stdout_pipe[1]);
      close_and_invalidate (&stderr_pipe[1]);
      close_and_invalidate (&status_pipe[1]);
      close_and_invalidate (&command_socket[0]);

      if (!read_ints (child_err_report_pipe[0],
                      buf, 2, &n_ints,
                      error))
        goto cleanup_and_fail;
        
      if (n_ints >= 2)
        {
          /* Error from the child. */

          switch (buf[0])
            {
            case CHILD_CHDIR_FAILED:
              g_set_error (error,
                           G_SPAWN_ERROR,
                           G_SPAWN_ERROR_CHDIR,
                           _("Failed to change to directory '%s' (%s)"),
                           working_directory,
                           g_strerror (buf[1]));

              break;
              
            case CHILD_EXEC_FAILED:
              g_set_error (error,
                           G_SPAWN_ERROR,
                           exec_err_to_g_error (buf[1]),
                           _("Failed to execute child process \"%s\" (%s)"),
                           argv[0],
                           g_strerror (buf[1]));

              break;
              
            case CHILD_DUP2_FAILED:
              g_set_error (error,
                           G_SPAWN_ERROR,
                           G_SPAWN_ERROR_FAILED,
                           _("Failed to redirect output or input of child process (%s)"),
                           g_strerror (buf[1]));

              break;

            case CHILD_FORK_FAILED:
              g_set_error (error,
                           G_SPAWN_ERROR,
                           G_SPAWN_ERROR_FORK,
                           _("Failed to fork child process (%s)"),
                           g_strerror (buf[1]));
              break;
              
            default:
              g_set_error (error,
                           G_SPAWN_ERROR,
                           G_SPAWN_ERROR_FAILED,
                           _("Unknown error executing child process \"%s\""),
                           argv[0]);
              break;
            }

          goto cleanup_and_fail;
        }

      /* Success against all odds! return the information */
      close_and_invalidate (&child_err_report_pipe[0]);
 
      *child_pid = pid;
      *standard_input = stdin_pipe[1];
      *standard_output = stdout_pipe[0];
      *standard_error = stderr_pipe[0];
      *status_fd = status_pipe[0];
      *command_fd = command_socket[1];
      
      return TRUE;
    }

 cleanup_and_fail:
  close_and_invalidate (&command_socket[0]);
  close_and_invalidate (&command_socket[1]);
  close_and_invalidate (&status_pipe[0]);
  close_and_invalidate (&status_pipe[1]);
  close_and_invalidate (&child_err_report_pipe[0]);
  close_and_invalidate (&child_err_report_pipe[1]);
  close_and_invalidate (&stdin_pipe[0]);
  close_and_invalidate (&stdin_pipe[1]);
  close_and_invalidate (&stdout_pipe[0]);
  close_and_invalidate (&stdout_pipe[1]);
  close_and_invalidate (&stderr_pipe[0]);
  close_and_invalidate (&stderr_pipe[1]);

  return FALSE;
}

static gboolean
make_pipe (gint     p[2],
           GError **error)
{
  if (pipe (p) < 0)
    {
      g_set_error (error,
                   G_SPAWN_ERROR,
                   G_SPAWN_ERROR_FAILED,
                   _("Failed to create pipe for communicating with child process (%s)"),
                   g_strerror (errno));
      return FALSE;
    }
  else
    return TRUE;
}

static gboolean
make_socketpair (gint     p[2],
		 GError **error)
{
  if (socketpair (PF_LOCAL, SOCK_STREAM, 0, p) < 0)
    {
      g_set_error (error,
                   G_SPAWN_ERROR,
                   G_SPAWN_ERROR_FAILED,
                   _("Failed to create pipe for communicating with child process (%s)"),
                   g_strerror (errno));
      return FALSE;
    }
  else
    return TRUE;
}

/* Based on execvp from GNU C Library */

static void
script_execute (const gchar *file,
                gchar      **argv,
                gchar      **envp,
                gboolean     search_path)
{
  /* Count the arguments.  */
  int argc = 0;
  while (argv[argc])
    ++argc;
  
  /* Construct an argument list for the shell.  */
  {
    gchar **new_argv;

    new_argv = g_new0 (gchar*, argc + 2); /* /bin/sh and NULL */
    
    new_argv[0] = (char *) "/bin/sh";
    new_argv[1] = (char *) file;
    while (argc > 0)
      {
	new_argv[argc + 1] = argv[argc];
	--argc;
      }

    /* Execute the shell. */
    if (envp)
      execve (new_argv[0], new_argv, envp);
    else
      execv (new_argv[0], new_argv);
    
    g_free (new_argv);
  }
}

static gchar*
my_strchrnul (const gchar *str, gchar c)
{
  gchar *p = (gchar*) str;
  while (*p && (*p != c))
    ++p;

  return p;
}

static gint
g_execute (const gchar *file,
           gchar      **argv,
           gchar      **envp,
           gboolean     search_path)
{
  if (*file == '\0')
    {
      /* We check the simple case first. */
      errno = ENOENT;
      return -1;
    }

  if (!search_path || strchr (file, '/') != NULL)
    {
      /* Don't search when it contains a slash. */
      if (envp)
        execve (file, argv, envp);
      else
        execv (file, argv);
      
      if (errno == ENOEXEC)
	script_execute (file, argv, envp, FALSE);
    }
  else
    {
      gboolean got_eacces = 0;
      const gchar *path, *p;
      gchar *name, *freeme;
      size_t len;
      size_t pathlen;

      path = g_getenv ("PATH");
      if (path == NULL)
	{
	  /* There is no `PATH' in the environment.  The default
	   * search path in libc is the current directory followed by
	   * the path `confstr' returns for `_CS_PATH'.
           */

          /* In GLib we put . last, for security, and don't use the
           * unportable confstr(); UNIX98 does not actually specify
           * what to search if PATH is unset. POSIX may, dunno.
           */
          
          path = "/bin:/usr/bin:.";
	}

      len = strlen (file) + 1;
      pathlen = strlen (path);
      freeme = name = g_malloc (pathlen + len + 1);
      
      /* Copy the file name at the top, including '\0'  */
      memcpy (name + pathlen + 1, file, len);
      name = name + pathlen;
      /* And add the slash before the filename  */
      *name = '/';

      p = path;
      do
	{
	  char *startp;

	  path = p;
	  p = my_strchrnul (path, ':');

	  if (p == path)
	    /* Two adjacent colons, or a colon at the beginning or the end
             * of `PATH' means to search the current directory.
             */
	    startp = name + 1;
	  else
	    startp = memcpy (name - (p - path), path, p - path);

	  /* Try to execute this name.  If it works, execv will not return.  */
          if (envp)
            execve (startp, argv, envp);
          else
            execv (startp, argv);
          
	  if (errno == ENOEXEC)
	    script_execute (startp, argv, envp, search_path);

	  switch (errno)
	    {
	    case EACCES:
	      /* Record the we got a `Permission denied' error.  If we end
               * up finding no executable we can use, we want to diagnose
               * that we did find one but were denied access.
               */
	      got_eacces = TRUE;

              /* FALL THRU */
              
	    case ENOENT:
#ifdef ESTALE
	    case ESTALE:
#endif
#ifdef ENOTDIR
	    case ENOTDIR:
#endif
	      /* Those errors indicate the file is missing or not executable
               * by us, in which case we want to just try the next path
               * directory.
               */
	      break;

	    default:
	      /* Some other error means we found an executable file, but
               * something went wrong executing it; return the error to our
               * caller.
               */
              g_free (freeme);
	      return -1;
	    }
	}
      while (*p++ != '\0');

      /* We tried every element and none of them worked.  */
      if (got_eacces)
	/* At least one failure was due to permissions, so report that
         * error.
         */
        errno = EACCES;

      g_free (freeme);
    }

  /* Return the error from the last attempt (probably ENOENT).  */
  return -1;
}
