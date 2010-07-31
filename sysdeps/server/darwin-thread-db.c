#ifndef _GNU_SOURCE
#define _GNU_SOURCE
#endif

#include <mach/mach.h>
#include "thread-db.h"
#include "darwin-ptrace.h"

ThreadDbHandle *
mono_debugger_thread_db_init (guint32 pid, GlobalLookupFunc global_lookup,
			      ReadMemoryFunc read_memory, WriteMemoryFunc write_memory)
{
	ThreadDbHandle *handle;
	thread_array_t threads;
	mach_msg_type_number_t count;
	kern_return_t err;

	handle = g_new0 (ThreadDbHandle, 1);
	handle->pid = GET_PID(pid);
	handle->global_lookup = global_lookup;
	handle->read_memory = read_memory;
	handle->write_memory = write_memory;
	if(task_for_pid(mach_task_self(), handle->pid, &handle->task) != KERN_SUCCESS) {
		g_warning (G_STRLOC ": Can't get Mach port for pid %d", handle->pid);
		return NULL;
	}

	err = task_threads(handle->task, &threads, &count);
	if (err) {		
		g_message (G_STRLOC ": task_threads failed: %d", err);
		return NULL;
	}
	
	err = vm_deallocate (mach_task_self(), (vm_address_t) threads, (count * sizeof (int)));
	if (err)
		g_message (G_STRLOC ": vm_deallocate failed: %d", err);

	return handle;
}

void
mono_debugger_thread_db_destroy (ThreadDbHandle *handle)
{
	g_free (handle);
}

/*
 * get_application_thread_port returns the thread port number in the
 * application's port namespace.  We get this so that we can present
 * the user with the same port number they would see if they store
 * away the thread id in their program.  It is for display purposes 
 * only.  
 */

gboolean
mono_debugger_thread_db_get_thread_info (const td_thrhandle_t *th, guint64 *tid, guint64 *tls,
					 guint64 *lwp)
{
	*tid = get_application_thread_port(((ThreadDbHandle*)th->handle)->task, th->tid);
	*tls = 0;
	int th_index = 0;
	if(get_thread_index(((ThreadDbHandle*)th->handle)->task, th->tid, &th_index))
		return FALSE;
		
	*lwp = COMPOSED_PID( ((ThreadDbHandle*)th->handle)->pid, th_index);

	return TRUE;
}

static int
iterate_over_threads_cb (const td_thrhandle_t *th, void *user_data)
{
	IterateOverThreadsFunc func = (IterateOverThreadsFunc) user_data;

	return (* func) (th) ? 0 : 1;
}

gboolean
mono_debugger_thread_db_iterate_over_threads (ThreadDbHandle *handle, IterateOverThreadsFunc func)
{
	thread_array_t threads;
	mach_msg_type_number_t count;
	kern_return_t err;
	int state_size = I386_THREAD_STATE_MAX;
	int i;

	err = task_threads(handle->task, &threads, &count);
	if (err)
		/* an error here usually means the task is already dead */
		return COMMAND_ERROR_UNKNOWN_ERROR;
	
	for(i = 0; i<count; i++) {
		td_thrhandle_t th;
		th.tid = threads[i];
		th.handle = handle;
		if(iterate_over_threads_cb(&th ,func))
			break;
	}
	
	err = vm_deallocate (mach_task_self(), (vm_address_t) threads, (count * sizeof (int)));
	if (err)
		g_message (G_STRLOC ": vm_deallocate failed: %d", err);
	  
	return COMMAND_ERROR_NONE;
}
