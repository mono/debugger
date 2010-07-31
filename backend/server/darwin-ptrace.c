#include <mach/mach.h>
#include <sys/sysctl.h>

#define THREAD_INDEX_MAX 1024
int g_thread_index_count = 0;
thread_t g_thread_list[THREAD_INDEX_MAX];
int g_thread_used_list[THREAD_INDEX_MAX];

struct InferiorList {
	InferiorHandle *inferior;
	struct InferiorList *next;
};
struct InferiorList *g_inferior_list = NULL;
GStaticMutex g_inferior_list_mutex = G_STATIC_MUTEX_INIT;

thread_t get_thread_from_index(int th_index)
{
	return g_thread_list[th_index];
}

GStaticMutex thread_id_mutex = G_STATIC_MUTEX_INIT;

ServerCommandError get_thread_index(mach_port_t task, thread_t thread, int *th_index)
{
	int i, j;
	g_static_mutex_lock (&thread_id_mutex);
	for(i=0; i<g_thread_index_count; i++) {
		if(g_thread_list[i] == thread) {
			*th_index = i;
			g_static_mutex_unlock (&thread_id_mutex);
			return COMMAND_ERROR_NONE;
		}
	}
	if(g_thread_index_count < THREAD_INDEX_MAX)
	{
		g_thread_list[g_thread_index_count] = thread;
		g_thread_used_list[g_thread_index_count] = TRUE;
		*th_index = g_thread_index_count++;
		g_static_mutex_unlock (&thread_id_mutex);
		return COMMAND_ERROR_NONE;
	}
	for(i=0; i<THREAD_INDEX_MAX; i++) {
		if(g_thread_used_list[i] == FALSE)
		{
			g_thread_list[i] = thread;
			g_thread_used_list[i] = TRUE;
			*th_index = i;
			g_static_mutex_unlock (&thread_id_mutex);
			return COMMAND_ERROR_NONE;
		}
	} 
	if(task) {
		thread_array_t threads;
		mach_msg_type_number_t count;
		kern_return_t err;
		int result = -1;
		
		err = task_threads(task, &threads, &count);
		if (err) {
			g_message (G_STRLOC ": task_threads failed: %d", err);
			return 0;
		}

		for(i=0; i<THREAD_INDEX_MAX; i++) {
			int found = FALSE;
			for(j=0; j<count; j++) {
				if(g_thread_list[i] == threads[i]) {
					found = TRUE;
					break;
				}
			}
			g_thread_used_list[i] = found;
			if(!found)
				result = i;
		}
	
		err = vm_deallocate (mach_task_self(), (vm_address_t) threads, (count * sizeof (int)));
		if (err)
			g_message (G_STRLOC ": vm_deallocate failed: %d", err);
			
		if(result == -1)
		{
			g_error (G_STRLOC ": no more thread indices!!!");
			g_static_mutex_unlock (&thread_id_mutex);
			return COMMAND_ERROR_UNKNOWN_ERROR;
		}
		g_thread_list[result] = thread;
		g_thread_used_list[result] = TRUE;
		*th_index = result;
		g_static_mutex_unlock (&thread_id_mutex);
		return COMMAND_ERROR_NONE;
	} else {
		g_error (G_STRLOC ": no more thread indices!!!");
		g_static_mutex_unlock (&thread_id_mutex);
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}
}

/* These are not defined in any header, although they are documented */
extern boolean_t exc_server(mach_msg_header_t *,mach_msg_header_t *);

static ServerCommandError
_server_ptrace_check_errno (InferiorHandle *inferior)
{
	gchar *filename;

	if (!errno)
		return COMMAND_ERROR_NONE;
	else if (errno != ESRCH) {
		g_message (G_STRLOC ": %d - %s", inferior->pid, g_strerror (errno));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	filename = g_strdup_printf ("/proc/%d/stat", inferior->pid);
	if (g_file_test (filename, G_FILE_TEST_EXISTS)) {
		g_free (filename);
		return COMMAND_ERROR_NOT_STOPPED;
	}

	g_warning (G_STRLOC ": %d - %s - %d (%s)", inferior->pid, filename,
		   errno, g_strerror (errno));
	g_free (filename);
	return COMMAND_ERROR_NO_TARGET;
}

static thread_t server_ptrace_get_inferior_primary_thread(InferiorHandle *inferior)
{
	thread_array_t threads;
	mach_msg_type_number_t count;
	kern_return_t err;
	thread_t result;
	
	err = task_threads(inferior->os.task, &threads, &count);
	if (err) {
		g_message (G_STRLOC ": task_threads failed: %d", err);
		return 0;
	}

	result = threads[0];
	
	err = vm_deallocate (mach_task_self(), (vm_address_t) threads, (count * sizeof (int)));
	if (err)
		g_message (G_STRLOC ": vm_deallocate failed: %d", err);
		
	return result;
}

static ServerCommandError
_server_ptrace_get_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs)
{
	kern_return_t err;
	mach_msg_type_number_t state_size = sizeof(x86_thread_state32_t)/sizeof(int);
	
	err = thread_get_state(inferior->os.thread, x86_THREAD_STATE32, (thread_state_t)regs, &state_size);
	if (err)
		return COMMAND_ERROR_UNKNOWN_ERROR;
		  
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
_server_ptrace_set_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs)
{
	kern_return_t err;
	
	err = thread_set_state(inferior->os.thread, x86_THREAD_STATE32, (thread_state_t)regs, sizeof(x86_thread_state32_t)/sizeof(int));
	if (err) {
		g_message (G_STRLOC ": thread_set_state failed: %s", mach_error_string(err));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}
				  
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
_server_ptrace_get_fp_registers (InferiorHandle *inferior, INFERIOR_FPREGS_TYPE *regs)
{
	kern_return_t err;
	mach_msg_type_number_t state_size = sizeof(x86_float_state32_t)/sizeof(int);
	
	err = thread_get_state(inferior->os.thread, x86_FLOAT_STATE32, (thread_state_t)regs, &state_size);
	if (err) {
		g_message (G_STRLOC ": thread_get_state failed: %s", mach_error_string(err));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}
	  
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
_server_ptrace_set_fp_registers (InferiorHandle *inferior, INFERIOR_FPREGS_TYPE *regs)
{
	kern_return_t err;

	err = thread_set_state(inferior->os.thread, x86_FLOAT_STATE32, (thread_state_t)regs, sizeof(x86_float_state32_t)/sizeof(int));
	if (err) {
		g_message (G_STRLOC ": thread_set_state failed: %s", mach_error_string(err));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}
	  
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
_server_ptrace_read_memory (ServerHandle *handle, guint64 start, guint32 size, gpointer buffer)
{
	kern_return_t err;
	vm_address_t low_address = (vm_address_t) trunc_page (start);
	vm_size_t aligned_length = (vm_size_t) round_page (start + size) - low_address;
	pointer_t copied;
	mach_msg_type_number_t copy_count;

	/* Get memory from inferior with page aligned addresses */
	err = vm_read (handle->inferior->os.task, low_address, aligned_length, &copied, &copy_count);
	if (err)
		return COMMAND_ERROR_MEMORY_ACCESS;
	
	memcpy (buffer, (void*)((vm_address_t)start - low_address + copied), size);

	err = vm_deallocate (mach_task_self (), copied, copy_count);
	if (err)
		g_warning (G_STRLOC ": vm_deallocate failed: %s", mach_error_string(err));
	
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_read_memory (ServerHandle *handle, guint64 start, guint32 size, gpointer buffer)
{
	ServerCommandError result = _server_ptrace_read_memory (handle, start, size, buffer);
	if (result != COMMAND_ERROR_NONE)
		return result;
	x86_arch_remove_breakpoints_from_target_memory (handle, start, size, buffer);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
_server_ptrace_make_memory_executable (ServerHandle *handle, guint64 start, guint32 size)
{
	kern_return_t err;
	vm_address_t low_address = (vm_address_t) trunc_page (start);
	vm_size_t aligned_length = (vm_size_t) round_page (start + size) - low_address;
	vm_size_t remaining_length = aligned_length;
	vm_address_t region_address = low_address;
		
	while (region_address < low_address + aligned_length) {
		mach_port_t object_name;
		vm_size_t region_length = remaining_length;
		
		vm_region_basic_info_data_64_t info;
		mach_msg_type_number_t info_cnt = VM_REGION_BASIC_INFO_COUNT_64;
		
		err = vm_region (handle->inferior->os.task, &region_address, &region_length, VM_REGION_BASIC_INFO_64, (vm_region_info_t) & info, &info_cnt, &object_name);
		if (err) {
			g_warning (G_STRLOC "vm_region failed: %s", mach_error_string(err));
			return COMMAND_ERROR_MEMORY_ACCESS;
		}

		if (!(info.protection & VM_PROT_EXECUTE)) {
			err = vm_protect (handle->inferior->os.task, region_address, region_length, FALSE, info.protection | VM_PROT_EXECUTE);
			if (err) {
				g_warning (G_STRLOC "vm_protect: restore protection failed: %s", mach_error_string(err));
				return COMMAND_ERROR_MEMORY_ACCESS;
			}
		}
						
		region_address += region_length;
		remaining_length = remaining_length - region_length;
	}
	
	return COMMAND_ERROR_NONE;	
}

struct vm_region_list
{
  struct vm_region_list *next;
  vm_prot_t protection;
  vm_address_t start;
  vm_size_t length;
};

static ServerCommandError
server_ptrace_write_memory (ServerHandle *handle, guint64 start,
			    guint32 size, gconstpointer buffer)
{
	kern_return_t err;
	vm_address_t low_address = (vm_address_t) trunc_page (start);
	vm_size_t aligned_length = (vm_size_t) round_page (start + size) - low_address;
	pointer_t copied;
	mach_msg_type_number_t copy_count;
	int fail = FALSE;

	err = vm_read (handle->inferior->os.task, low_address, aligned_length, &copied, &copy_count);
	if (err) {
		g_warning (G_STRLOC "vm_read failed: %s", mach_error_string(err));
		return COMMAND_ERROR_MEMORY_ACCESS;
	}
	
	memcpy ( (void *) ((vm_address_t)start - low_address + copied), buffer, size);

	{
		vm_size_t remaining_length = aligned_length;
		vm_address_t region_address = low_address;
		struct vm_region_list *region_element;
		struct vm_region_list *region_head = (struct vm_region_list *) NULL;
		
		struct vm_region_list *scan;
		
		while (region_address < low_address + aligned_length) {
			mach_port_t object_name;
			vm_size_t region_length = remaining_length;
			
			vm_region_basic_info_data_64_t info;
			mach_msg_type_number_t info_cnt = VM_REGION_BASIC_INFO_COUNT_64;
			
			err = vm_region (handle->inferior->os.task, &region_address, &region_length, VM_REGION_BASIC_INFO_64, (vm_region_info_t) & info, &info_cnt, &object_name);
			if (err) {
				g_warning (G_STRLOC "vm_region failed: %s", mach_error_string(err));
				fail = TRUE;
				break;
			}
			
			region_element = (struct vm_region_list *) malloc (sizeof (struct vm_region_list));
			
			region_element->protection = info.protection;
			region_element->start = region_address;
			region_element->length = region_length;
			region_element->next = region_head;
			
			region_head = region_element;
			region_address += region_length;
			remaining_length = remaining_length - region_length;
		}
		
		if (!fail) {
			for (scan = region_head; scan; scan = scan->next) {
				if (!(scan->protection & VM_PROT_WRITE)) {
					err = vm_protect (handle->inferior->os.task, scan->start, scan->length, FALSE, scan->protection | VM_PROT_WRITE);
					if(err) {
						g_warning (G_STRLOC "vm_protect: enable write failed: %s", mach_error_string(err));
						fail = TRUE;
					}
				}
			}
			
			err = vm_write (handle->inferior->os.task, low_address, copied, aligned_length);
			if (err) {
				g_warning (G_STRLOC "vm_write failed: %s", mach_error_string(err));
				fail = TRUE;
			}
			
			for (scan = region_head; scan; scan = scan->next) {
				if (!(scan->protection & VM_PROT_WRITE)) {
					err = vm_protect (handle->inferior->os.task, scan->start, scan->length, FALSE, scan->protection);
					if (err) {
						g_warning (G_STRLOC "vm_protect: restore protection failed: %s", mach_error_string(err));
						fail = TRUE;
					}
				}
			}
		}
	}

	err = vm_deallocate (mach_task_self (), copied, copy_count);
	if (err) {
		g_warning (G_STRLOC "vm_deallocate failed: %d", err);
		fail = TRUE;
	}

	return fail ? COMMAND_ERROR_MEMORY_ACCESS : COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_poke_word (ServerHandle *handle, guint64 addr, gsize value)
{
	return server_ptrace_write_memory (handle, addr, sizeof(value), &value);
}

static ServerCommandError
_server_ptrace_set_dr (InferiorHandle *inferior, int regnum, guint64 value)
{
	kern_return_t err;
	mach_msg_type_number_t state_size = sizeof(x86_debug_state32_t)/sizeof(int);
	x86_debug_state32_t state;
	
	err = thread_get_state(inferior->os.thread, x86_DEBUG_STATE32, (thread_state_t)&state, &state_size);
	if (err) {		
		g_message (G_STRLOC ": thread_get_state failed: %s, %d", mach_error_string(err), inferior->os.thread);
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}
		
	(&state.__dr0)[regnum] = value;

	err = thread_set_state(inferior->os.thread, x86_DEBUG_STATE32, (thread_state_t)&state, state_size);
	if (err) {		
		g_message (G_STRLOC ": thread_set_state failed: %s", mach_error_string(err));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}
	
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
_server_ptrace_get_dr (InferiorHandle *inferior, int regnum, guint64 *value)
{
	kern_return_t err;
	mach_msg_type_number_t state_size = sizeof(x86_debug_state32_t)/sizeof(int);
	x86_debug_state32_t state;
	
	err = thread_get_state(inferior->os.thread, x86_DEBUG_STATE32, (thread_state_t)&state, &state_size);
	if (err) {
		g_message (G_STRLOC ": thread_get_state failed: %s", mach_error_string(err));
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}
		
	*value = (&state.__dr0)[regnum];
	  
	return COMMAND_ERROR_NONE;
}

GStaticMutex wait_mutex = G_STATIC_MUTEX_INIT;
GStaticMutex wait_mutex_2 = G_STATIC_MUTEX_INIT;
GStaticMutex wait_mutex_3 = G_STATIC_MUTEX_INIT;

thread_t g_wait_thread = -1;
thread_t g_stopped_thread = -1;
thread_t g_stepping_thread = -1;
mach_port_t g_stepping_task = -1;
static guint32
do_wait (int pid, int *status)
{
	int ret;
#if DEBUG_WAIT
	g_message (G_STRLOC ": do_wait (%d)", pid);
#endif
	ret = waitpid (pid, status, WUNTRACED);	
#if DEBUG_WAIT
	g_message (G_STRLOC ": do_wait (%d) finished: %d - %x", pid, ret, *status);
#endif
	if (ret < 0) {
		if (errno == EINTR)
			return 0;
		else if (errno == ECHILD)
			return -1;
		g_warning (G_STRLOC ": Can't waitpid for %d: %s", pid, g_strerror (errno));
		return -1;
	}

	if(g_stepping_thread != -1) {
		thread_array_t threads;
		mach_msg_type_number_t count;
		kern_return_t err;
		int i;
				
		err = task_threads(g_stepping_task, &threads, &count);
		if (err == KERN_SUCCESS) {
			for(i=0; i<count; i++) {
				if(threads[i] != g_stepping_thread)
					thread_resume (threads[i]);
			}

			err = vm_deallocate (mach_task_self(), (vm_address_t) threads, (count * sizeof (int)));
			if (err)
				g_warning (G_STRLOC ": vm_deallocate failed: %d", err);
		}

		g_stepping_thread = -1;
	}
	
	if(WIFSTOPPED (*status) && WSTOPSIG (*status) == SIGSTOP)
		return g_stopped_thread;

	if(g_wait_thread == -1)
		return ret;
	
	if(get_thread_index(0, g_wait_thread, &pid))
		return 0;
		
	return COMPOSED_PID(ret, pid);
}

static int stop_requested = 0;
static int stop_status = 0;

static guint32
server_ptrace_global_wait (guint32 *status_ret)
{
	int ret, status;

 again:
	g_static_mutex_lock (&wait_mutex);
	ret = do_wait (-1, &status);
	if (ret <= 0)
		goto out;

#if DEBUG_WAIT
	g_message (G_STRLOC ": global wait finished: %d - %x", ret, status);
#endif

	g_static_mutex_lock (&wait_mutex_2);

#if DEBUG_WAIT
	g_message (G_STRLOC ": global wait finished #1: %d - %x - %d",
		   ret, status, stop_requested);
#endif

	if (ret == stop_requested) {
		*status_ret = 0;
		stop_status = status;
		g_static_mutex_unlock (&wait_mutex_2);
		g_static_mutex_unlock (&wait_mutex);

		g_static_mutex_lock (&wait_mutex_3);
		g_static_mutex_unlock (&wait_mutex_3);
		goto again;
	}
	g_static_mutex_unlock (&wait_mutex_2);

	*status_ret = status;
 out:
	g_static_mutex_unlock (&wait_mutex);
	return ret;
}

static ServerCommandError
server_ptrace_stop (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;
	struct task_basic_info info;
	mach_msg_type_number_t info_size = sizeof(struct task_basic_info)/sizeof(int);

	inferior->os.wants_to_run = FALSE;

	if (task_info(inferior->os.task, TASK_BASIC_INFO, (task_info_t)&info, &info_size) == KERN_SUCCESS)
	{
		if(info.suspend_count > 0)
		{
			thread_suspend (inferior->os.thread);
			/* 
			 * if we stop the thread this way, we cannot receive signals of it anymore. 
			 * so, send an already stopped error, to let the debugger take care of us. 
			 */
			return COMMAND_ERROR_ALREADY_STOPPED;
		}
		if (syscall (SYS_kill, inferior->pid, SIGSTOP)) {
			/*
			 * It's already dead.
			 */
			if (errno == ESRCH)
				return COMMAND_ERROR_NO_TARGET;
			else
				return COMMAND_ERROR_UNKNOWN_ERROR;
		}

		g_stopped_thread = COMPOSED_PID(inferior->pid, inferior->os.thread_index);
	}
	
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_stop_and_wait (ServerHandle *handle, guint32 *status)
{
	/*
	 * stop_and_wait doesn't make sense on OS X, since we are not guaranteed that wait will
	 * return the same thread. Just make it the same as stop, and have the wait loop catch the 
	 * signal.
	 */
	
	*status = NULL;
	
	return server_ptrace_stop (handle);
}

thread_t
get_application_thread_port (mach_port_t task, thread_t our_name)
{
	mach_msg_type_number_t i;
	mach_port_name_array_t names;
	mach_msg_type_number_t names_count;
	mach_port_type_array_t types;
	mach_msg_type_number_t types_count;
	mach_port_t match = 0;
	kern_return_t ret;

	/*
	 * To get the application name, we have to iterate over all the ports
	 * in the application and extract a right for them.  The right will include
	 * the port name in our port namespace, so we can use that to find the
	 * thread we are looking for.  Of course, we don't actually need another
     * right to each of these ports, so we deallocate it when we are done.  
	 */

	ret = mach_port_names (task, &names, &names_count, &types, &types_count);
	if (ret != KERN_SUCCESS) {
		  g_warning (G_STRLOC ": Error %d getting port names from mach_port_names", ret);
		  return (thread_t) 0x0;
	}

	for (i = 0; i < names_count; i++) {
		mach_port_t local_name;
		mach_msg_type_name_t local_type;

		ret = mach_port_extract_right (task, names[i], MACH_MSG_TYPE_COPY_SEND, &local_name, &local_type);

		if (ret == KERN_SUCCESS) {
			mach_port_deallocate (mach_task_self (), local_name);
			if (local_name == our_name) {
				match = names[i];
				break;
			}
		}
	}

	vm_deallocate (mach_task_self (), (vm_address_t) names,
				 names_count * sizeof (mach_port_t));

	return (thread_t) match;
}

/*
 * These structures and techniques are illustrated in
 * Mac OS X Internals, Amit Singh, ch 9.7
 */
typedef struct {
  mach_msg_header_t           header;
  mach_msg_body_t             body;
  mach_msg_port_descriptor_t  thread;
  mach_msg_port_descriptor_t  task;
  NDR_record_t                ndr;
  exception_type_t            exception;
  mach_msg_type_number_t      code_count;
  integer_t                   code[EXCEPTION_CODE_MAX];
  char                        padding[512];
} exception_message_t;

void* server_mach_msg_rcv_thread(void *p)
{
	ServerHandle *handle = (ServerHandle*)p;

	while(1) {
		kern_return_t err;
		exception_message_t msg, reply;

		err = mach_msg (&msg.header, MACH_RCV_MSG | MACH_RCV_INTERRUPT | MACH_RCV_TIMEOUT, 0, sizeof (msg), handle->inferior->os.exception_port, 100, MACH_PORT_NULL);
		if(err == MACH_RCV_TIMED_OUT)
		{
			if(handle->inferior->os.stop_exception_thread)
				break;
			continue;
		}
		if(err != KERN_SUCCESS)
			g_warning (G_STRLOC ": mach_msg: %s", mach_error_string(err));
			
		g_wait_thread = msg.thread.name;
		
		if(!exc_server(&msg.header, &reply.header))
			g_warning (G_STRLOC ": exc_server failed");
        
 		err = mach_msg (&reply.header, MACH_SEND_MSG, reply.header.msgh_size, 0, MACH_PORT_NULL, MACH_MSG_TIMEOUT_NONE, MACH_PORT_NULL);
		if(err != KERN_SUCCESS)
			g_warning (G_STRLOC ": mach_msg: %s", mach_error_string(err));
	}
}

static ServerCommandError
_server_ptrace_setup_inferior (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;
	kern_return_t err;

	err = task_for_pid(mach_task_self(), inferior->pid, &inferior->os.task);
	if(err != KERN_SUCCESS) {
		g_warning (G_STRLOC ": Can't get Mach task for pid %d: %s. \n" 
					"If you get an error here, that is usually because the mono runtime executable has not"
					"been codesigned, or has been signed with an untrusted certificate."
					, inferior->pid, mach_error_string(err));
		return COMMAND_ERROR_UNKNOWN_ERROR;		
	}
	
	if(inferior->os.thread == 0) {
		err = mach_port_allocate (mach_task_self (), MACH_PORT_RIGHT_RECEIVE, &inferior->os.exception_port);
		if(err != KERN_SUCCESS) {
			g_warning (G_STRLOC ": mach_port_allocate: %s", mach_error_string(err));
			return COMMAND_ERROR_UNKNOWN_ERROR;		
		}

		err = mach_port_insert_right (mach_task_self (), inferior->os.exception_port, inferior->os.exception_port, MACH_MSG_TYPE_MAKE_SEND);
		if(err != KERN_SUCCESS) {
			g_warning (G_STRLOC ": mach_port_insert_right: %s", mach_error_string(err));
			return COMMAND_ERROR_UNKNOWN_ERROR;		
		}
								
		err = task_set_exception_ports (inferior->os.task, EXC_MASK_ALL, inferior->os.exception_port, EXCEPTION_DEFAULT, THREAD_STATE_NONE);
		if(err != KERN_SUCCESS) {
			g_warning (G_STRLOC ": task_set_exception_ports: %s", mach_error_string(err));
			return COMMAND_ERROR_UNKNOWN_ERROR;		
		}

		inferior->os.stop_exception_thread = FALSE;
		inferior->os.thread = server_ptrace_get_inferior_primary_thread(inferior);
		pthread_create(&inferior->os.exception_thread, NULL, server_mach_msg_rcv_thread, handle);

		g_wait_thread = inferior->os.thread;
	}
	else
		inferior->os.exception_thread = 0;
	
	if(get_thread_index(inferior->os.task, inferior->os.thread, &inferior->os.thread_index))
		return COMMAND_ERROR_UNKNOWN_ERROR;

	inferior->os.wants_to_run = TRUE;

	g_static_mutex_lock (&g_inferior_list_mutex);

	struct InferiorList **inferior_list_iterator = &g_inferior_list;
	
	while (*inferior_list_iterator != NULL)
		inferior_list_iterator = &((*inferior_list_iterator)->next);
	
	(*inferior_list_iterator) = g_malloc (sizeof (struct InferiorList));
	(*inferior_list_iterator)->inferior = inferior;
	(*inferior_list_iterator)->next = NULL;
	
	g_static_mutex_unlock (&g_inferior_list_mutex);	
	
	return COMMAND_ERROR_NONE;
}

static void
_server_ptrace_finalize_inferior (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;
	if(inferior->os.exception_thread)
	{
		inferior->os.stop_exception_thread = TRUE;
		pthread_join(inferior->os.exception_thread, NULL);
	}

	g_static_mutex_lock (&g_inferior_list_mutex);

	struct InferiorList **inferior_list_iterator = &g_inferior_list;
	
	while (*inferior_list_iterator != inferior && *inferior_list_iterator != NULL)
		inferior_list_iterator = &((*inferior_list_iterator)->next);
	
	if (*inferior_list_iterator != NULL) {
		g_free (*inferior_list_iterator);
		*inferior_list_iterator = (*inferior_list_iterator)->next;
	}
	
	g_static_mutex_unlock (&g_inferior_list_mutex);	
}

static ServerCommandError
server_ptrace_initialize_process (ServerHandle *handle)
{
	/* 
	 * Darwin/Mach hasn't implemented the extended ptrace events (PTRACE_O_TRACECLONE at al). 
	 * So, nothing to do here
	 */

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_get_signal_info (ServerHandle *handle, SignalInfo **sinfo_out)
{
	SignalInfo *sinfo = g_new0 (SignalInfo, 1);

	sinfo->sigkill = SIGKILL;
	sinfo->sigstop = SIGSTOP;
	sinfo->sigint = SIGINT;
	sinfo->sigchld = SIGCHLD;

	sinfo->sigfpe = SIGFPE;
	sinfo->sigquit = SIGQUIT;
	sinfo->sigabrt = SIGABRT;
	sinfo->sigsegv = SIGSEGV;
	sinfo->sigill = SIGILL;
	sinfo->sigbus = SIGBUS;

	/* __SIGRTMIN is the hard limit from the kernel, SIGRTMIN is the first
	 * user-visible real-time signal.  __SIGRTMIN and __SIGRTMIN+1 are used
	 * internally by glibc. */
	sinfo->kernel_sigrtmin = SIGUSR1;
#ifdef USING_MONO_FROM_TRUNK
	sinfo->mono_thread_abort = -1;
#else
	sinfo->mono_thread_abort = mono_thread_get_abort_signal ();
#endif

	*sinfo_out = sinfo;

	return COMMAND_ERROR_NONE;
}

static void
server_ptrace_global_init (void)
{
	stop_requested = 0;
	stop_status = 0;
}

static ServerCommandError
server_ptrace_get_threads (ServerHandle *handle, guint32 *out_count, guint32 **out_threads)
{
	InferiorHandle *inferior = handle->inferior;
	thread_array_t threads;
	mach_msg_type_number_t count;
	kern_return_t err;
	int state_size = I386_THREAD_STATE_MAX;
	int i;

	err = task_threads(inferior->os.task, &threads, &count);
	if (err)
		return COMMAND_ERROR_UNKNOWN_ERROR;
	
	*out_threads = g_new0 (guint32, count);
	*out_count = count;
	for(i = 0; i<count; i++) {
		int th_index;
		if(get_thread_index(inferior->os.task, threads[i], &th_index))
			continue;
		(*out_threads) [i] = GPOINTER_TO_UINT (COMPOSED_PID(inferior->pid, th_index));
	}
	
	err = vm_deallocate (mach_task_self(), (vm_address_t) threads, (count * sizeof (int)));
	if (err)
		g_message (G_STRLOC ": vm_deallocate failed: %d", err);
	  
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_get_application (ServerHandle *handle, gchar **exe_file, gchar **cwd,
			       guint32 *nargs, gchar ***cmdline_args)
{
    int					mib[4];
	GPtrArray *array;
	gchar **ptr;	
	int maxarg = 0, numArgs = 0, i;
	size_t size = 0;
	char *args = NULL, *cp = NULL, *arg = NULL;
	    
    mib[0] = CTL_KERN;
    mib[1] = KERN_ARGMAX;
    
    size = sizeof(maxarg);
    if ( sysctl(mib, 2, &maxarg, &size, NULL, 0) == -1 ) {
		g_warning (G_STRLOC ": sysctl failed.");
		return COMMAND_ERROR_UNKNOWN_ERROR;		
    }
    
    args = (char *)malloc( maxarg );
    if ( args == NULL ) {
		g_warning (G_STRLOC ": malloc failed.");
		return COMMAND_ERROR_UNKNOWN_ERROR;		
    }
    
    mib[0] = CTL_KERN;
    mib[1] = KERN_PROCARGS2;
    mib[2] = handle->inferior->pid;
    
    size = (size_t)maxarg;
    if ( sysctl(mib, 3, args, &size, NULL, 0) == -1 ) {
		free( args );
		g_warning (G_STRLOC ": sysctl failed.");
		return COMMAND_ERROR_UNKNOWN_ERROR;		
     }
    
    memcpy( &numArgs, args, sizeof(numArgs) );
    cp = args + sizeof(numArgs);
    
    arg = cp;
 	array = g_ptr_array_new ();
   
	for ( i = 0; cp < &args[size]; cp++ ) {
		if ( *cp == '\0' ) {
			if ( (arg != NULL) && (*arg != '\0') ) {
				if(i == 0)
					*exe_file = g_strdup (arg);
				else if(i <= numArgs)
					g_ptr_array_add (array,  g_strdup (arg) );
				else
					break;
				while ( ((*cp == '\0') && (cp < &args[size])) )
					cp++;
				arg = cp;
				i++;
			}
		}
    }

	//@todo: implement!
	*cwd = g_strdup("/");

	*nargs = array->len;
	*cmdline_args = ptr = g_new0 (gchar *, array->len + 1);

	for (i = 0; i < array->len; i++)
		ptr  [i] = g_ptr_array_index (array, i);

	free(args);
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_detach_after_fork (ServerHandle *handle)
{
	ServerCommandError result;
	GPtrArray *breakpoints;
	int status;
	int ret, i;

	ret = waitpid (handle->inferior->pid, &status, WUNTRACED | WNOHANG /*| __WALL | __WCLONE*/);
	if (ret < 0)
		g_warning (G_STRLOC ": Can't waitpid for %d: %s", handle->inferior->pid, g_strerror (errno));

	/*
	 * Make sure we're stopped.
	 */
	if (x86_arch_get_registers (handle) != COMMAND_ERROR_NONE)
		do_wait (handle->inferior->pid, &status);

	result = x86_arch_get_registers (handle);
	if (result != COMMAND_ERROR_NONE)
		return result;

	mono_debugger_breakpoint_manager_lock ();

	breakpoints = mono_debugger_breakpoint_manager_get_breakpoints (handle->bpm);
	for (i = 0; i < breakpoints->len; i++) {
		BreakpointInfo *info = g_ptr_array_index (breakpoints, i);

		x86_arch_disable_breakpoint (handle, info);
	}

	mono_debugger_breakpoint_manager_unlock ();

	if (ptrace (PT_DETACH, handle->inferior->pid, NULL, 0) != 0)
		return _server_ptrace_check_errno (handle->inferior);

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_continue (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;
	INFERIOR_REGS_TYPE regs;
	struct task_basic_info info;
	mach_msg_type_number_t info_size = sizeof(struct task_basic_info)/sizeof(int);
	kern_return_t err;

	struct thread_basic_info th_info;
	unsigned int info_count = THREAD_BASIC_INFO_COUNT;

	/* Clear trap flag, if in case it had been set in server_ptrace_step */
	_server_ptrace_get_registers(inferior, &regs);
	regs.eflags &= ~0x100UL;
	_server_ptrace_set_registers(inferior, &regs);

	err = thread_info (inferior->os.thread, THREAD_BASIC_INFO, (thread_info_t) &th_info, &info_count);
	if (err)
		g_warning (G_STRLOC ": thread_info failed: %s",mach_error_string(err));
	else while(th_info.suspend_count > 0) {
		th_info.suspend_count--;
		thread_resume (inferior->os.thread);
	}

	inferior->stepping = FALSE;
	inferior->os.wants_to_run = TRUE;
	
	g_static_mutex_lock (&g_inferior_list_mutex);

	struct InferiorList *inferior_list_iterator = g_inferior_list;

	int wants_to_run = TRUE;
	while (inferior_list_iterator != NULL)
	{
		if (inferior_list_iterator->inferior->pid == inferior->pid)
			wants_to_run &= inferior_list_iterator->inferior->os.wants_to_run;
		inferior_list_iterator = (inferior_list_iterator)->next;
	}
	
	g_static_mutex_unlock (&g_inferior_list_mutex);	

	if (!wants_to_run)
		return COMMAND_ERROR_NONE;		
	
	errno = 0;

	if (ptrace (PT_CONTINUE, inferior->pid, (caddr_t) 1, inferior->last_signal)) {
		return _server_ptrace_check_errno (inferior);
	}

	/* Make sure we actually resume, in case task_suspend has been called once too much. */
	task_info(inferior->os.task, TASK_BASIC_INFO, (task_info_t)&info, &info_size);
	while(info.suspend_count > 0) {
		info.suspend_count--;
		task_resume(inferior->os.task);
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_step (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;
	thread_array_t threads;
	mach_msg_type_number_t count;
	kern_return_t err;
	INFERIOR_REGS_TYPE regs;
	int i;
	
	/* 
	 * PT_STEP seems to be badly broken on OS X in multi-threaded environments.
	 * When using it on anything but the main thread, it kernel panics.
	 * This can be fixed by disabling all other threads. But then, still it will not
	 * perform the step. This can be fixed by manually setting the trap flag of that thread.
	 * All of these have to be reset when continuing normal operation (ie in server_ptrace_continue).  
	 */	
	
	err = task_threads(inferior->os.task, &threads, &count);
	if (err) {
		g_message (G_STRLOC ": task_threads failed: %d", err);
		return COMMAND_ERROR_UNKNOWN_ERROR;
	}

	for(i=0; i<count; i++)
		thread_suspend (threads[i]);

	struct thread_basic_info info;
	unsigned int info_count = THREAD_BASIC_INFO_COUNT;
	thread_info (inferior->os.thread, THREAD_BASIC_INFO, (thread_info_t) &info, &info_count);
	while(info.suspend_count > 0) {
		info.suspend_count--;
		thread_resume (inferior->os.thread);
	}
	g_stepping_thread = inferior->os.thread;
	g_stepping_task = inferior->os.task;
	
	_server_ptrace_get_registers(inferior, &regs);
	regs.eflags |= 0x100UL;
	_server_ptrace_set_registers(inferior, &regs);

	errno = 0;
	inferior->stepping = TRUE;

	if (ptrace (PT_STEP, inferior->pid, (caddr_t) 1, inferior->last_signal))
		return _server_ptrace_check_errno (inferior);

	err = vm_deallocate (mach_task_self(), (vm_address_t) threads, (count * sizeof (int)));
	if (err)
		g_message (G_STRLOC ": vm_deallocate failed: %d", err);
	
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_ptrace_kill (ServerHandle *handle)
{
	InferiorHandle *inferior = handle->inferior;
	thread_array_t threads;
	mach_msg_type_number_t count;
	kern_return_t err;
	struct thread_basic_info th_info;
	unsigned int info_count = THREAD_BASIC_INFO_COUNT;
	int i;

	kill (inferior->pid, SIGKILL);

	if (task_threads(inferior->os.task, &threads, &count) == KERN_SUCCESS) {
		for(i=0; i<count; i++) {
			if (thread_info (threads[i], THREAD_BASIC_INFO, (thread_info_t) &th_info, &info_count) == KERN_SUCCESS) {
				th_info.suspend_count--;
				thread_resume (threads[i]);
			}
		}
	}
	
	ptrace (PTRACE_KILL, handle->inferior->pid, NULL, 0);

	return COMMAND_ERROR_NONE;
}

static ServerType
server_ptrace_get_server_type (void)
{
	return SERVER_TYPE_DARWIN;
}

static ServerCapabilities
server_ptrace_get_capabilities (void)
{
	return SERVER_CAPABILITIES_NONE;
}
