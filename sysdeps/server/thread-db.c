#ifndef _GNU_SOURCE
#define _GNU_SOURCE
#endif

#include <thread-db.h>

ps_err_e
ps_pglobal_lookup (ThreadDbHandle *handle, const char *object_name,
		   const char *sym_name, psaddr_t *sym_addr)
{
	guint64 address;
	ps_err_e e;

	e = (* handle->global_lookup) (object_name, sym_name, &address);
	*sym_addr = GUINT_TO_POINTER ((gsize) address);
	return e;
}

ps_err_e
ps_pdread (ThreadDbHandle *handle, psaddr_t addr, void *buffer, size_t size)
{
	return (* handle->read_memory) ((guint64) (gsize) addr, buffer, size);
}

ps_err_e
ps_pdwrite (ThreadDbHandle *handle, psaddr_t addr, const void *buffer, size_t size)
{
	return (* handle->write_memory) ((guint64) (gsize) addr, buffer, size);
}

ps_err_e
ps_lgetregs (ThreadDbHandle *handle, lwpid_t lwp, prgregset_t regs)
{
	return PS_ERR;
}

ps_err_e
ps_lsetregs (ThreadDbHandle *handle, lwpid_t lwp, const prgregset_t regs)
{
	return PS_ERR;
}

ps_err_e
ps_lgetfpregs (ThreadDbHandle *handle, lwpid_t lwp, prfpregset_t *regs)
{
	return PS_ERR;
}

ps_err_e
ps_lsetfpregs (ThreadDbHandle *handle, lwpid_t lwp, const prfpregset_t *regs)
{
	return PS_ERR;
}

pid_t
ps_getpid (ThreadDbHandle *handle)
{
	return handle->pid;
}

ThreadDbHandle *
mono_debugger_thread_db_init (guint32 pid, GlobalLookupFunc global_lookup,
			      ReadMemoryFunc read_memory, WriteMemoryFunc write_memory)
{
	ThreadDbHandle *handle;
	td_err_e e;

	e = td_init ();
	if (e)
		return NULL;

	handle = g_new0 (ThreadDbHandle, 1);
	handle->pid = pid;
	handle->global_lookup = global_lookup;
	handle->read_memory = read_memory;
	handle->write_memory = write_memory;

	e = td_ta_new (handle, &handle->thread_agent);
	if (e)
		return NULL;

	return handle;
}

void
mono_debugger_thread_db_destroy (ThreadDbHandle *handle)
{
	td_ta_delete (handle->thread_agent);
	g_free (handle);
}

gboolean
mono_debugger_thread_db_get_thread_info (const td_thrhandle_t *th, guint64 *tid, guint64 *tls,
					 guint64 *lwp)
{
	td_thrinfo_t ti;
	td_err_e e;

	e = td_thr_get_info (th, &ti);
	if (e)
		return FALSE;

	*tid = (guint64) (gsize) ti.ti_tid;
	*tls = (guint64) (gsize) ti.ti_tls;
	*lwp = ti.ti_lid;

	return TRUE;
}

static int
iterate_over_threads_cb (const td_thrhandle_t *th, void *user_data)
{
	IterateOverThreadsFunc func = (IterateOverThreadsFunc) user_data;
	td_thrinfo_t ti;
	td_err_e e;

	return (* func) (th) ? 0 : 1;
}

gboolean
mono_debugger_thread_db_iterate_over_threads (ThreadDbHandle *handle, IterateOverThreadsFunc func)
{
	td_thrhandle_t th;
	td_err_e e;

	e = td_ta_thr_iter (handle->thread_agent, iterate_over_threads_cb, func,
			    TD_THR_ANY_STATE, TD_THR_LOWEST_PRIORITY, TD_SIGNO_MASK,
			    TD_THR_ANY_USER_FLAGS);

	return e == PS_OK;
}
