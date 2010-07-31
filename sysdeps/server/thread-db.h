#ifndef __MONO_DEBUGGER_THREAD_DB_H__
#define __MONO_DEBUGGER_THREAD_DB_H__

#include <server.h>
#include <pthread.h>

#ifdef __linux__
#include <thread_db.h>
#include "linux-proc-service.h"
#else
typedef enum
{
  PS_OK,		/* Generic "call succeeded".  */
  PS_ERR,		/* Generic error. */
  PS_BADPID,		/* Bad process handle.  */
  PS_BADLID,		/* Bad LWP identifier.  */
  PS_BADADDR,		/* Bad address.  */
  PS_NOSYM,		/* Could not find given symbol.  */
  PS_NOFREGS		/* FPU register set not available for given LWP.  */
} ps_err_e;

typedef struct{
	thread_t tid;
	void *handle;
} td_thrhandle_t;
#endif

G_BEGIN_DECLS

/* C# delegates. */
typedef ps_err_e (*GlobalLookupFunc) (const char *object_name, const char *sym_name, guint64 *addr);
typedef ps_err_e (*ReadMemoryFunc) (guint64 address, void *buffer, guint32 size);
typedef ps_err_e (*WriteMemoryFunc) (guint64 address, const void *buffer, guint32 size);

typedef gboolean (*IterateOverThreadsFunc) (const td_thrhandle_t *th);

typedef struct ps_prochandle {
	pid_t pid;
#ifdef __MACH__
	mach_port_t task;
#else
	td_thragent_t *thread_agent;
#endif
	GlobalLookupFunc global_lookup;
	ReadMemoryFunc read_memory;
	WriteMemoryFunc write_memory;
} ThreadDbHandle;

ThreadDbHandle *
mono_debugger_thread_db_init (guint32 pid, GlobalLookupFunc global_lookup,
			      ReadMemoryFunc read_memory, WriteMemoryFunc write_memory);

void
mono_debugger_thread_db_destroy (ThreadDbHandle *handle);

gboolean
mono_debugger_thread_db_get_thread_info (const td_thrhandle_t *th, guint64 *tid, guint64 *tls,
					 guint64 *lwp);

gboolean
mono_debugger_thread_db_iterate_over_threads (ThreadDbHandle *handle, IterateOverThreadsFunc cb);

G_END_DECLS

#endif
