#include <server.h>
#include "x86-arch.h"
#include <assert.h>
#include <windows.h>
#include <Psapi.h>
#include <stdio.h>
#include <glib.h>
#define BREAKPOINT_HIT_DEBUG_EVENT	0x10000
/* REMARKS 
    should one drop all Windows specific data-types and use the glib counterparts e.g
	guint32 instead of DWORD? CHECK */
 /* Base idea of the whole approach is to 
   1) have one Debug "master" 
   2) which steers all threads created during debugging
   3) minimize sharing (therefor I will  integrate information I'd like to keep
   directly into the structures of mdb but just on demand and just so much that the rest 
   of the program can proceed
 */
/*

/* Naming conventions used 
   * globals are Prefixed with g
   * globals for this Module are prefixed with m
   * structures do  end in _rec
   * typedefs struct int UPPER_CASE 
   * constants are written in upper case separated by _
   * Camelcase is not used, '_' separates words. Of course in
     calls to the Windows API CamelsCase must be used. That's how things are
	 done on Windows 
   * the return values for Windows functions are prefixed with the type and then _rval is appended
     so e.g a BOOL return value should be named b_rval.
   * if the name of the variable is more important but the type, the type may be appended
     this is usefulf for 'thread_handle' and 'thread_id' ....
   * names are normally wriiten exceptions are buffers which often are just named buf...
   * ATTENTION points to places we've not yet fully understand 
   * REMINDER or REMIND is a note to look there during development
   * CHECK maybe a trouble spot 
   * TODO reminder on things which need some work
*/

/* global event handling array, for signalling from one thread to the other */

#define G_COMMAND_EVENTS_MAX 6
enum EVENTS {EVENT_RUNNING, EVENT_RESUME, EVENT_RESULT, 
			 EVENT_MEMORY_READ, EVENT_MEMORY_WRITE, 
			 EVENT_QUEUE, EVENT_KILL};
static HANDLE m_command_events [G_COMMAND_EVENTS_MAX];
/* has the debugger attached to the debuggee? */

enum ATTACH_STATUS {NOT_ATTACHED, ATTACHED};
enum RW_FLAG {READ,  WRITE};
static enum ATTACH_STATUS g_attached; /* is the debuggeee attached? */
static BOOL g_paused; /* is the debugged processed paused currently? */

#define DEBUG_EVENT_WAIT_VALUE 100000
#define BP_OPCODE 0xCC  /* INT 3 instruction */
#define TF_BIT 0x100    /* single-step register bit */

/* is there something else to use in Mono? CHECK */
#define BPFLAG_ONCEONLY			1
#define BPFLAG_USERDEFINED		2
#define BPFLAG_DISABLED			4
#define BPFLAG_BPBYADDRESS		8
#define BPFLAG_SKIPCALL			16
#define BPFLAG_SYSTEMCALL		32
#define BPFLAG_STEPOUT			64
#define BPFLAG_SETRACE			128
#define BPFLAG_SESTART			256
#define BPFLAG_SESKIPINIT		512
#define BPFLAG_SEBREAKPOINT		1024
#define BPFLAG_SEEXCEPTION		2048
#define BPFLAG_SEERRORN			4096
#define BPFLAG_IMPORT			8192
#define BPFLAG_HASCOUNT			0x4000
#define BPFLAG_FNCALL			0x8000


#define INFERIOR_REGS_TYPE	CONTEXT
/* in Windows it's just the  CONTEXT  with different flags so 
 we have either to extend get_set_registers with flags or 
 have to write another fucntion get_set_floating_point_registers CHECK */
// #define INFERIOR_FPREGS_TYPE	struct user_fpregs_struct




#if 0
/* handle to the Debugger Thread which  will steer everything */
HANDLE g_debugging_thread;
#endif
/* important this keeps a record of all the threads the debugging thread may
   have created so we'll have a list of this structures in which we add new thread
   which are created under supverison of the one debugging thread */
struct thread_rec {
	HANDLE thread_handle;
	DWORD thread_id;
	void *local_base;
	int stopped;
	int suspended_count;
	guint32 intial_sp;

};
/* structure used for sending informations between 
the debugger thread and the debuggee thread */
struct info_buf_rec {
	guint64 start;
	guint32 size;
	gpointer buffer;
	guint32 read_or_written_bytes;

};

struct function_decriptor_rec {
	char *name;
	guint32 address;
	unsigned short type;
	guint32 debug_start;
	guint32 debug_end;
	/* guint32 CodeEnd; */
	unsigned short number_of_breakpoint_lines;
	unsigned short *first_line_number;
	guint32 *first_line_offset;
	// LocalVariable *Automatics;
	// short scope;
	unsigned short last_valid_line;
	guint32 ebp;		/* value of EBP when active */
	guint32 current_eip; /* value of continuation when in stack */
	// struct tagSourceFile *SourceFile;
};


struct current_function_rec {
	// MODULE *module;
	struct function_decriptor_rec  *function_descriptor;
	/* what structure in mdb ? PBPNODE LastBreakpoint;  */
	guint32 last_eip;
	guint32 eip;
	guint32 ebp;
	guint32 start_address;
	guint32 end_address;
	char in_single_step;
	char trace_flag;
	char step_flag;
};




/* buffer for information exchange */
struct info_buf_rec m_info_buf;
DWORD m_windows_event_code;


struct ArchInfo
{
	INFERIOR_REGS_TYPE current_regs; /* we need them in Windows also but have to fetch them differently TODO */
	// INFERIOR_FPREGS_TYPE current_fpregs;
	GPtrArray *callback_stack;
	//CodeBufferData *code_buffer;
	//guint64 dr_control, dr_status;
	int dr_regs [DR_NADDR];
};

/* Data for the process which is debugged (debuggee) */
struct debuggee_info_rec {
	HANDLE process_handle;      /* process under scrutiny */
	DWORD process_id; /* process id from the to be debugged process */
	HANDLE thread_handle;
	DWORD thread_id; /* thread id of the debugged process not the debugger thread id ! */
	enum ATTACH_STATUS debuggee_attached; /* is the debuggee attached to the debugging thread ? */
	CONTEXT context; /* context of the debugged processs */
	PBYTE start_address;  /* address of main () */
	guint32 dll_load_address;
	PBYTE image_base;
	guint32 last_eip; /* qualifiying to save the eip of the debuggeee */
	guint32 eip;
	struct current_function_rec current_function;
	unsigned int inaccessible_flag;
	gint8 one_shot_step; 
	gint8 breakpoint_hit;
	gint8 created_new_process;
	guint8 is_attached; /* have we attached to a the program? */
	guint8 stripped_exe; /* do we have debug information present ? */
	guint32 start_of_code;	
	guint32 last_valid_source_line;
};

/* structure for keeping information about the debugged process this will 
change considerably over time see especially the Inferior.cs file for information about it
I'll keep this informaton in a global Variable since I know better how to map from/to C# objects */

typedef struct debug_info_rec {
	ServerHandle *server_handle; /* handle to the information which are used in other parts
						 I keep it in this structure and will fill it as need be from other 
						 entries in this structure. */
	HANDLE debugger_handle; /* handle for the debugger thread if this will be needed is not clear */
	DWORD debugger_id ; /* thread id of the debugger thread */
	/* maybe use of some Structure ? REMIND */
	struct debuggee_info_rec debuggee;

	GList *thread_list; /* this is the list to save all the thread_records 
	I'm using a glib double linked list here */
	
	
	/* information below are probably not to be kept here, but
	   because I'm not sure about it let it stay here */
	const char ** argv; /* argument vector for starting the debugger */
	wchar_t * w_argv_flattened; /* for handing over to CreateProcess */
	wchar_t *w_envp_flattened; /* envp as wide char and one long string for CreateProcess */
	int argc; /* argument count */
	int redireckt_fds; /* are the file descriptors be redirected */
	const char ** envp; /* pointer to the environment, probably not needed */
	const wchar_t * w_working_directory; /* working directory for the debugged process */
} DEBUG_INFO;

struct sys_dll_info_rec {
	void *file_base_address;
	IMAGE_SYMBOL *symbol_table; // needed? frido
	FPO_DATA *stack_frame_infos;
	int number_of_symbols;
	void *cv_infos; /* CHECK frido */
};

struct loaded_dll_info_rec {
	char *name;
	guint32 base;
	HANDLE file_handle;
	guint32 code_start;
	guint32 size;
	guint32 end_of_code;
	int debug_info_file_offset;
	char *debug_info_file;
	PIMAGE_DEBUG_DIRECTORY debug_dir;
	int debug_formats_count;
	struct sys_dll_info_rec * sys_dll_info;
	guint32 DirectHits;
	//int COFFSymbolCount;
	HANDLE file_mapping_handle;
	char *file_base_ptr;
	int number_of_modules;
	// MODULE *Modules; // needed in mdb?
	int has_cv_info;
	//GlobalTypesInfo *GlobalTypes;
	//GlobalSymbol **GlobalSymbols;
	//CoffDebugInfo coffInfo;
};

/* this surely will be needed */
#if 0
typedef enum {
	DEBUGGER_REG_RAX	= 0,
	DEBUGGER_REG_RCX,
	DEBUGGER_REG_RDX,
	DEBUGGER_REG_RBX,

	DEBUGGER_REG_RSP,
	DEBUGGER_REG_RBP,
	DEBUGGER_REG_RSI,
	DEBUGGER_REG_RDI,

	DEBUGGER_REG_R8,
	DEBUGGER_REG_R9,
	DEBUGGER_REG_R10,
	DEBUGGER_REG_R11,
	DEBUGGER_REG_R12,
	DEBUGGER_REG_R13,
	DEBUGGER_REG_R14,
	DEBUGGER_REG_R15,

	DEBUGGER_REG_RIP,
	DEBUGGER_REG_EFLAGS,

	DEBUGGER_REG_ORIG_RAX,
	DEBUGGER_REG_CS,
	DEBUGGER_REG_SS,
	DEBUGGER_REG_DS,
	DEBUGGER_REG_ES,
	DEBUGGER_REG_FS,
	DEBUGGER_REG_GS,

	DEBUGGER_REG_FS_BASE,
	DEBUGGER_REG_GS_BASE,

	DEBUGGER_REG_LAST
} DebuggerX86Registers;
#endif

enum __ptrace_eventcodes {
  PTRACE_EVENT_FORK     = 1,
  PTRACE_EVENT_VFORK    = 2,
  PTRACE_EVENT_CLONE    = 3,
  PTRACE_EVENT_EXEC     = 4,
  PTRACE_EVENT_VFORK_DONE = 5,
  PTRACE_EVENT_EXIT     = 6
};


/* a list of the above loaded_dll_info_rec for looking up Symbols 
   maybe this is done differently in mdb so CHECK */
static GSList * m_loaded_dlls;

static wchar_t windows_error_message [2048];
static const size_t windows_error_message_len = 2048;

/* this global has to be replaced and put in the proper structure. e.g into the Inferior part
but how and which information should be placed there is not yet clear. To shield clients from
internal changes accessor function will be used. This accessor functions are yet to be written and 
one must expect them to change over time */
static DEBUG_INFO m_debug_info;
static GAsyncQueue *bridge;




/* Prototypes */
/* preparing a debugger thread and starting the process to be debugged */
static int start_debugging (DEBUG_INFO *); 
/* Event loop for the debugger thread */
DWORD debugging_thread (LPVOID);
/* creation of the debuggee */
static int launch_debuggee (void);
/* Format error message to a more readable form */
static void format_windows_error_message (DWORD error_code);
/* events initialization */
static int initialize_events (void);
static int get_set_registers (CONTEXT *context,  enum READ_FLAG flag);
static int single_step_checks (guint32 eip);
static BOOL decrement_ip (HANDLE debuggee_thread_handle);
static int resume_from_imports_table (int flag);
static BreakpointInfo* on_breakpoint_exception (DWORD address,int *b_continue);


static ServerCommandError 
read_write_memory (ServerHandle *server_handle, guint64 start, guint32 size, gpointer buffer, int read) ;
static BOOL read_from_debuggee (LPVOID start, LPVOID buf, DWORD size, PDWORD read_bytes) ;

/* prototyps for the Function table */
static ServerCommandError
server_win32_read_memory (ServerHandle *handle, guint64 start, guint32 size, gpointer buffer);
static ServerCommandError
server_win32_write_memory (ServerHandle *handle, guint64 start, guint32 size, gconstpointer buffer);

static ServerCommandError
server_win32_insert_breakpoint (ServerHandle *handle, guint64 address, guint32 *bhandle);


ArchInfo *
x86_arch_initialize (void)
{
	ArchInfo *arch = g_new0 (ArchInfo, 1);
	arch->callback_stack = g_ptr_array_new ();
	return arch;
}
/* accessors for the DEBUG_INFO structure this may turn out to be a bad idea, if
we decide that the structure will be local to the debugging thread, but for now just use it,  we assume that
just he debugger thread accesses this accessors. Again our understanding of the surrounding C# code and calls into
this parts is not clear */
void set_debugger_handle_and_thread_id (HANDLE dbg_handle, DWORD dbg_handle_tid) 
{
	m_debug_info.debugger_handle = dbg_handle;
	m_debug_info.debugger_id = dbg_handle_tid;
}

void set_debuggee_process_information (PROCESS_INFORMATION *pi) {

	m_debug_info.debuggee.process_handle = pi->hProcess;
	m_debug_info.debuggee.thread_handle = pi->hThread;
	m_debug_info.debuggee.process_id  = pi->dwProcessId;
	m_debug_info.debuggee.thread_id = pi->dwThreadId;
}

void set_debuggee_process_id (DWORD pid) 
{
	m_debug_info.debuggee.process_id = pid;
}


/* Set ups the events array which are used to allow communication betwween threads.
   returns 1 on success
   0 on failure
*/
static int initialize_events (void) 
{
	int result = 1;
	enum Event ev;

  /* only EVENT_RUNNING uses manual reset */
  m_command_events [EVENT_RUNNING] = CreateEvent (NULL, TRUE, FALSE, NULL);
  m_command_events [EVENT_RESUME]  = CreateEvent (NULL, FALSE, FALSE, NULL);
  m_command_events [EVENT_RESULT]  = CreateEvent (NULL, FALSE, FALSE, NULL);
  m_command_events [EVENT_MEMORY_READ] = CreateEvent (NULL, FALSE, FALSE, NULL);
  m_command_events [EVENT_MEMORY_WRITE] = CreateEvent (NULL, FALSE, FALSE, NULL);
  m_command_events [EVENT_KILL]    = CreateEvent (NULL, FALSE, FALSE, NULL);
  m_command_events [EVENT_QUEUE] = CreateEvent (NULL, FALSE, FALSE, NULL);

  /* were any events not created? */
  for (ev=EVENT_RUNNING; ev< G_COMMAND_EVENTS_MAX; ev++) {
	  if (! m_command_events [ev]){	
		result = 0;
		break;
	  }
  }
  /* if any were missed, destroy all of them */
	if (! result){
	  for (ev=EVENT_RUNNING; ev< G_COMMAND_EVENTS_MAX; ev++) {
		if (m_command_events [ev]) CloseHandle (m_command_events [ev]);
	  }
	}
  return (result);

}


BOOL set_step_flag (BOOL on)
{
    CONTEXT context;
    BOOL b_rval;
    context.ContextFlags = CONTEXT_CONTROL|CONTEXT_INTEGER|
							CONTEXT_SEGMENTS|CONTEXT_FLOATING_POINT;
	b_rval = GetThreadContext (m_debug_info.debuggee.thread_handle, &context);
    if (b_rval) {
        if (on) {
            context.EFlags |= TF_BIT;
		}
        else {
            context.EFlags &= ~TF_BIT;
			// Debuggee.SingleStepThreadId = 0;
			// may have to put this  value into the DEBUG_INFO struct just left out for 
			// now
		}
		b_rval = SetThreadContext (m_debug_info.debuggee.thread_handle, &context);
			// Debuggee.hThread, &Context); 
    }
	if (! b_rval) {
		/* where to print error messages in mdb CHECK */
			fprintf (stderr, "Impossible to put debuggee in single step\n");

	}
	else {
		/* save the current context for internal use */
		memcpy (&m_debug_info.debuggee.context,&context,sizeof (CONTEXT));
	}
    return (b_rval);
}

static BOOL set_all_code_pages_access (int flag)
{
	MEMORY_BASIC_INFORMATION buffer;
	/* save in mdb to use static variabls here CHECK */
	static DWORD dw_original_flags;
	DWORD dw_new_flags,dw_old_flags;
	SIZE_T st_rval;
	PBYTE address;

	memset (&buffer,0,sizeof (MEMORY_BASIC_INFORMATION));
	st_rval = VirtualQueryEx (m_debug_info.debuggee.process_handle,
							 (LPCVOID)m_debug_info.debuggee.start_of_code,
							&buffer,
							sizeof (MEMORY_BASIC_INFORMATION));

	if (0 == st_rval) {
		g_debug ("VirtualQueryEx failed, permission problem?\n");
		return 0;
	}
	if (flag)
		dw_new_flags = PAGE_NOACCESS;
	else
		dw_new_flags = dw_original_flags;
	address = buffer.BaseAddress;
	SetLastError (0);
	st_rval = VirtualProtectEx (m_debug_info.debuggee.process_handle, 
								address, buffer.RegionSize,
                                dw_new_flags, &dw_old_flags);
	if (0 == st_rval) {
		format_windows_error_message (GetLastError ());
		return 0;
	}
	if (dw_original_flags == 0)
		dw_original_flags = dw_old_flags; 
	m_debug_info.debuggee.inaccessible_flag = flag;
	return 1;
}


static void set_stepping (int stepping)
{
	m_debug_info.debuggee.current_function.in_single_step = stepping;
}

static int get_stepping (void) 
{
	return m_debug_info.debuggee.current_function.in_single_step;
}


guint32 activate_thread (DWORD thread_id) 
{
	GList *it = g_list_first (m_debug_info.thread_list);
	struct thread_rec *thread;
	while (it) {
	    thread = it->data;
		if (thread->thread_id == thread_id) {
			m_debug_info.debuggee.thread_handle = thread->thread_handle;
			m_debug_info.debuggee.thread_id = thread->thread_id;
			return thread->intial_sp;
		}
		it = g_list_next (it);
	}
	return 0;
}


/* function for handling debug events like
   single stepping, reaching a breakpoint etc */
static BOOL do_debug_exception_event (LPDEBUG_EVENT pde, HANDLE debuggee_handle,int *unknown_exception)
{
    BOOL b_continue = TRUE;
	guint32 initial_sp=0;
	BreakpointInfo *breakpoint;
    int save_thread_id;
	HANDLE save_thread_handle;
	UINT exception_code = pde->u.Exception.ExceptionRecord.ExceptionCode;
    guint32 exception_address = (guint32)
		pde->u.Exception.ExceptionRecord.ExceptionAddress;
	if (m_debug_info.debuggee.process_id != 0 && pde->dwProcessId != m_debug_info.debuggee.process_id)
		return TRUE;
	m_debug_info.debuggee.last_eip = m_debug_info.debuggee.eip;
	m_debug_info.debuggee.eip = (guint32) exception_address;
    
	save_thread_handle = 	m_debug_info.debuggee.thread_handle; 
	save_thread_id = m_debug_info.debuggee.thread_id;

	
	if (m_debug_info.debuggee.thread_id != pde->dwThreadId) {
		initial_sp = activate_thread (pde->dwThreadId);
	}
	
    switch (exception_code) {
    case EXCEPTION_BREAKPOINT:
        breakpoint = on_breakpoint_exception (exception_address, &b_continue);

        break;
    case EXCEPTION_SINGLE_STEP:
		/* data breakpoint handling
		if (Debuggee.StepOverDataBP) {
			Debuggee.StepOverDataBP = 0;
			RestoreDataBreakpoints ();
		}
		ReadAllRegisters (&Debuggee.Context);
		if (!CurrentFunction.inSingleStep && (Debuggee.Context.Dr6&0xF)) {
			if (HandleDataBreakpoint ()) {
				bContinue = FALSE;
				break;
			}
		}
		*/
        /* restore the breakpoint we just stepped over */

/*        if (pbpPending)
            SetBreakpoint (hDebuggee, pbpPending);
			*/
        // pbpPending = NULL;
		if (m_debug_info.debuggee.one_shot_step) {
			m_debug_info.debuggee.one_shot_step=0;
			/* not ideal to many indirections for direct access IMPROVE */
			set_stepping (0);
			b_continue = TRUE;
			if (resume_from_imports_table (0)) {
				set_all_code_pages_access (1);
				set_step_flag (0);
			} else 
			   // We arrive hre when we have entered an unknown function, and it wasn't a system
			   // function but some other library function for which we have no debug info.
			   // Establish a breakpoint in the return address at *esp.
			   // doing that in mono? CHECK 
			   // SetupBreakpointAtReturnAddress ();
			break;
		}
        /* end single-step mode */
		if (0 == get_stepping ()) {
            set_step_flag (FALSE);
		}
        if (get_stepping ()) {
			/* do we need this checks in mdb and if yes how 
			   to integrate TODO */
			/*
            if (! (r = SingleStepChecks (Debuggee.Context.Eip))) {
				bContinue = FALSE;
				break;
			}
            else {
			
                if (r == 1)
                    SetStepFlag (TRUE);
				Debuggee.hThread = saveThreadHandle;
				Debuggee.dwThreadId = saveThreadId;
                return (TRUE);
            }
			*/
        }
        b_continue = TRUE;
        break;
    case EXCEPTION_ACCESS_VIOLATION:
		if (m_debug_info.debuggee.inaccessible_flag) {
			set_stepping (1);			
			set_all_code_pages_access (0);
			set_step_flag (1);
			
			/* how to  do that in mono CHECK */
			/*
			if (!FindCurrentSourceLine (Debuggee.Context.Eip)) {
				FindCurrentFunctionInStack (1);
			}
			*/
			b_continue = TRUE;
			break;
		}
	default:
		*unknown_exception = 1;
		if (pde->u.Exception.dwFirstChance)
			return TRUE;
        b_continue = FALSE;
		g_paused = TRUE; /* name should start with m_ it's module global */
		// ShowExceptionViolation (pde);
		/* this should probably be added out our debug_info  record.  */
		/*
		ReadAllRegisters (&Debuggee.TrapContext);
		ReadAllRegisters (&Debuggee.Context);
		*/
		/*
		  should the disassembly be done here or 
		   is that another area of mdb 
		if (initial_sp) {
			save = m_debug_info.debuggee.initial_sp;
			m_debug_info.debuggee.initial_sp = initial_sp;
			Debuggee.InitialSP = initialsp;
		}
		if (!FindCurrentFunctionInStack (1)) {
			if (Debuggee.StrippedExe) {
				PostMessage (hWedit,WM_COMMAND,CMD_DISASSEMBLY,0);
			}
		}
		if (initialsp) {
			Debuggee.InitialSP = save;
		}
		UpdateDisplays ();
		*/
        break;
    }
	m_debug_info.debuggee.thread_handle = save_thread_handle;
	m_debug_info.debuggee.thread_id = save_thread_id;
    return (b_continue);
}


/* helper to just print what event has occurred */
static void show_debug_event (DEBUG_EVENT * debug_event)
{
    DWORD exception_code;

    
    switch (debug_event->dwDebugEventCode) {
    case EXCEPTION_DEBUG_EVENT:
		exception_code = debug_event->u.Exception.ExceptionRecord.ExceptionCode;
		if (exception_code== EXCEPTION_BREAKPOINT || exception_code == EXCEPTION_SINGLE_STEP)
			return;
		// DecodeException (e,tampon);
		g_debug ( "ExceptionCode: %d at address 0x%p",exception_code, 
				debug_event->u.Exception.ExceptionRecord.ExceptionAddress);

	   
        if (debug_event->u.Exception.dwFirstChance != 0)
			g_debug ("First Chance\n");
        else
			g_debug ("Second Chance \n");
            
		
        break;
        // ------------------------------------------------------------------
        // new thread started
        // ------------------------------------------------------------------
    case CREATE_THREAD_DEBUG_EVENT:
        g_debug ("Creating thread %d: hThread: %p,\tLocal base: %p, start at %p",
                 debug_event->dwThreadId,
				 debug_event->u.CreateThread.hThread,
                 debug_event->u.CreateThread.lpThreadLocalBase,
				 debug_event->u.CreateThread.lpStartAddress);
		   /* thread  list !! to be places not in the show helper AddThread (DebugEvent); REMIND */
        break;
        // ------------------------------------------------------------------
        // new process started
        // ------------------------------------------------------------------
    case CREATE_PROCESS_DEBUG_EVENT:
		if (m_debug_info.debuggee.process_handle == 0) {
	        g_debug (
				"CreateProcess:\thProcess: %p\thThread: %p\n + %s%p\t%s%d"
                 "\n + %s%d\t%s%p\n + %s%p\t%s%p\t%s%d",
					debug_event->u.CreateProcessInfo.hProcess,
					debug_event->u.CreateProcessInfo.hThread,
                 TEXT ("Base of image:"), debug_event->u.CreateProcessInfo.lpBaseOfImage,
                 TEXT ("Debug info file offset: "), debug_event->u.CreateProcessInfo.dwDebugInfoFileOffset,
                 TEXT ("Debug info size: "), debug_event->u.CreateProcessInfo.nDebugInfoSize,
                 TEXT ("Thread local base:"), debug_event->u.CreateProcessInfo.lpThreadLocalBase,
                 TEXT ("Start Address:"), debug_event->u.CreateProcessInfo.lpStartAddress,
                 TEXT ("Image name:"), debug_event->u.CreateProcessInfo.lpImageName,
				 TEXT ("fUnicode: "), debug_event->u.CreateProcessInfo.fUnicode);
		} else {
			g_debug ("Create Process %d. hProcess %p",debug_event->dwProcessId,debug_event->u.CreateProcessInfo.hProcess);

		}
		break;
        // ------------------------------------------------------------------
        // existing thread terminated
        // ------------------------------------------------------------------
    case EXIT_THREAD_DEBUG_EVENT:
        g_debug ("Thread %d finished with code %d",
             debug_event->dwThreadId, debug_event->u.ExitThread.dwExitCode);
		/* thread list deletion placement DeleteThreadFromList (DebugEvent); REMND */
        break;
        // ------------------------------------------------------------------
        // existing process terminated
        // ------------------------------------------------------------------
    case EXIT_PROCESS_DEBUG_EVENT:
        g_debug ("Exit Process %d Exit code: %d",debug_event->dwProcessId,debug_event->u.ExitProcess.dwExitCode);
		
        break;
        // ------------------------------------------------------------------
        // new DLL loaded
        // ------------------------------------------------------------------
    case LOAD_DLL_DEBUG_EVENT:
        g_debug ("Load DLL: Base %p",debug_event->u.LoadDll.lpBaseOfDll);
		break;
        // ------------------------------------------------------------------
        // existing DLL explicitly unloaded
        // ------------------------------------------------------------------
    case UNLOAD_DLL_DEBUG_EVENT:
		g_debug ("Unload DLL: base %p",debug_event->u.UnloadDll.lpBaseOfDll);
		break;
        // ------------------------------------------------------------------
        // OutputDebugString () occured
        // ------------------------------------------------------------------
    case OUTPUT_DEBUG_STRING_EVENT:
		{
			g_debug ("OUTPUT_DEBUG_STRNG_EVENT\n");
			/* could be useful but left out for the moment */
			
        }
        break;
        // ------------------------------------------------------------------
        // RIP occured
        // ------------------------------------------------------------------
    case RIP_EVENT:
		g_debug ("RIP:\n + %s%d\n + %s%d",
                 TEXT ("dwError: "), debug_event->u.RipInfo.dwError,
                 TEXT ("dwType: "), debug_event->u.RipInfo.dwType);
        break;
        // ------------------------------------------------------------------
        // unknown debug event occured
        // ------------------------------------------------------------------
    default:
        g_debug ("%s%X%s",
                 TEXT ("Debug Event:Unknown [0x"),
                 debug_event->dwDebugEventCode, "",
                 TEXT ("]"));
        break;
    }
    
}

static int add_dll_to_dll_list (DEBUG_EVENT *debug_event, char * dll_name) 
{
	struct loaded_dll_info_rec *dll_info;
	int result = 1;
	struct debuggee_info_rec save_debuggee;
	
	/* check if the DLL has debug information if not we do not 
	have to copy save this DLL it's with high likliness a system dll from Windows */
	if (debug_event->u.LoadDll.dwDebugInfoFileOffset == 0) {
		CloseHandle (debug_event->u.LoadDll.hFile);
		return 0;
	}
	
	memcpy (&save_debuggee,&m_debug_info.debuggee,sizeof (struct debuggee_info_rec));
	dll_info = g_new0 (struct loaded_dll_info_rec, 1);
	
	// memset (&Debuggee.coffInfo,0,sizeof (CoffDebugInfo));
	dll_info->name = g_strdup (dll_name);
	dll_info->base = (guint32) debug_event->u.LoadDll.lpBaseOfDll;
	dll_info->file_handle =  debug_event->u.LoadDll.hFile;
	dll_info->debug_info_file_offset;
	m_debug_info.debuggee.dll_load_address = dll_info->base;
	
    /* now this is difficult. What is this in mdb, from where do we get this information
	and do we need it read here or is there a function just for that
	C or is this done in C# CHECK */
	// result= FillDllInfo (info);
	m_loaded_dlls = g_slist_prepend (m_loaded_dlls, dll_info);
	/* info->Next = Dlls;
	Dlls = info;
	info->GlobalTypes = GlobalTypes;
	info->GlobalSymbols = GlobalSymbols;
	info->hFileMapping = Debuggee.hFileMapping;
	GlobalTypes = saveGlobalTypes;
	GlobalSymbols = saveGlobalSymbols;
	// how to do cleanup here?
	if (result == 0) {
		CloseHandle (Debuggee.hFileMapping);
		UnmapViewOfFile (Debuggee.lpFileBase);
		info->hFileMapping = NULL;

	}
	*/
	// memcpy (&Debuggee,&saveDebuggee,sizeof (Debuggee));
	return result;


}
static void add_to_bridge (int status)
{
	if (! bridge) {
		bridge =  g_async_queue_new ();
	}
    g_assert (bridge);
	g_async_queue_lock (bridge);

	g_async_queue_push_unlocked (bridge, GINT_TO_POINTER (status));
	g_async_queue_unlock (bridge);
}

static void check_for_debug_event (HANDLE debuggee_handle) 
{
    DEBUG_EVENT debug_event;
	struct thread_rec *thread_rec;
    BOOL b_continue = TRUE;
	int i_rval,status;
	BOOL b_rval;
	DWORD exc_code = 0;
	DWORD st_read_bytes = 0;
	ServerCommandError sce_rval = 0;
	char buf [1024];
	wchar_t w_buf [1024];
	// needed? PBPNODE pBP;
	int unknown_exception;
    /* wait up to DEBUG_EVENT_WAIT_VALLUE ms for a debug event to occur */
	if (WaitForDebugEvent (&debug_event, DEBUG_EVENT_WAIT_VALUE)) {
		/*
		if (ProfileFlag) {
			WaitValue = !WaitValue;
		}
		*/
		/* informational printout of the occurred event */
		show_debug_event (&debug_event);
		// maybe something for that above would be helpful....
        /* determine what event occurred */
        switch (debug_event.dwDebugEventCode) {
        case EXCEPTION_DEBUG_EVENT:
			if (debug_event.u.Exception.ExceptionRecord.ExceptionCode == EXCEPTION_ACCESS_VIOLATION) {
				/* &&
				! (DebuggerFlags & DBG_FLAG_TRAP_ALL) &&
				Debuggee.bInaccessibleFlag == 0 &&
				DebugEvent.u.Exception.dwFirstChance) {
				 ATTENTION how to do that in mdb */
				/*
				Do not single step into the exception handler!
				*/
				set_step_flag (0);
				ContinueDebugEvent (debug_event.dwProcessId,
                                   debug_event.dwThreadId, 
								   DBG_EXCEPTION_NOT_HANDLED);
				return;
			}
		
			unknown_exception = 0;
            b_continue = do_debug_exception_event (&debug_event, debuggee_handle,&unknown_exception);
			if (unknown_exception && debug_event.u.Exception.dwFirstChance) {
				ContinueDebugEvent (debug_event.dwProcessId,
                               debug_event.dwThreadId, DBG_EXCEPTION_NOT_HANDLED);
				return;

			}
			
            break;
        case CREATE_PROCESS_DEBUG_EVENT:
			/* again how to do in mdb ? */
			if (m_debug_info.debuggee.breakpoint_hit > 0) {
				m_debug_info.debuggee.created_new_process = 1;
				break;
			}
			
			g_attached = ATTACHED;
			g_paused = TRUE;
			
			m_debug_info.debuggee.start_address = (PBYTE) debug_event.u.CreateProcessInfo.lpStartAddress;
			m_debug_info.debuggee.image_base = (PBYTE) debug_event.u.CreateProcessInfo.lpBaseOfImage;
			/*
			if (Debuggee.isAttached) {
				Debuggee.hProcess = DebugEvent.u.CreateProcessInfo.hProcess;
			}
            else
				Debuggee.hProcess = hDebuggee;
		
            Debuggee.dwProcessId = DebugEvent.dwProcessId;
            Debuggee.hThread = DebugEvent.u.CreateProcessInfo.hThread;
			Debuggee.hFile = DebugEvent.u.CreateProcessInfo.hFile;
            Debuggee.dwThreadId = DebugEvent.dwThreadId;
			 
            Debuggee.bBreakSeen = FALSE;
			*/
			thread_rec = g_new0 (struct thread_rec, 1);
			g_assert (thread_rec != NULL);
			thread_rec->thread_handle = m_debug_info.debuggee.thread_handle;
			thread_rec->thread_id = m_debug_info.debuggee.thread_id;
			m_debug_info.thread_list = g_list_append (m_debug_info.thread_list, (gpointer) thread_rec);
			/* checking if 
			   a) the first entry always is the debuggee 
			   b) that the element was properly added
			   CHECK */
			status = debug_event.dwDebugEventCode;
			add_to_bridge (status);

            break;
		case LOAD_DLL_DEBUG_EVENT: 
			if (debug_event.u.LoadDll.lpImageName) {
				b_rval = read_from_debuggee (debug_event.u.LoadDll.lpImageName, &exc_code, 4, &st_read_bytes);
				/* what's a reasonable way of handing an error occurring here */
				if (exc_code) {
					b_rval = read_from_debuggee ( (LPVOID) exc_code, buf, 300, &st_read_bytes);
					b_rval = read_from_debuggee ( (LPVOID) exc_code, w_buf, 300, &st_read_bytes);
					if (debug_event.u.LoadDll.fUnicode) {
						/* CHECK numbers */
						char *tmp_buf = g_malloc (300);
						memcpy (tmp_buf,buf, 300);
						wcstombs (buf, (unsigned short *)tmp_buf,300);
						g_free (tmp_buf);
					}
				} else {
					/* how to get the dll name properly in the 
					    generated code */
					/*
					 if (GetModuleFileNameFromHeader (debug_event->dwProcessId,
                                            debug_event->u.LoadDll.hFile,
					          (guint32) debug_event->u.LoadDll.lpBaseOfDll,
						                        buf, 300))
					} else g_debug ("impossible to get dll name");
				}*/
				}
			}
        
			i_rval = add_dll_to_dll_list (&debug_event,buf);
			break;
        case EXIT_PROCESS_DEBUG_EVENT:
			if (debug_event.dwProcessId != m_debug_info.debuggee.process_id) {
				b_continue = TRUE;
				break;
			}
            g_attached = NOT_ATTACHED;
			g_paused = TRUE;
			
			fprintf (stderr, "debug_event.u.ExitProcess.dwExitCode = %ul\n", debug_event.u.ExitProcess.dwExitCode);
			return;
        }   /* end switch (EventCode) */
		
        /* Unless the debuggee is paused at a */
        /* breakpoint, resume execution of debuggee */
        if (b_continue) {
            ContinueDebugEvent (debug_event.dwProcessId,
                               debug_event.dwThreadId, DBG_CONTINUE);
		} else {
            g_paused = TRUE;
			ResetEvent (m_command_events [EVENT_RUNNING]);
			// Debuggee.CurrentStoppedThreadId = DebugEvent.dwThreadId;
			//  does one have to store this in the mdb context also?
			
        }
    } /* if WaitFor.... */
}


static void do_terminate_process (HANDLE process_handle)
{
	
	int i=0;
	// GlobalSymbol *pExitPoint=NULL;
	//guint32 stopAddress;

	TerminateProcess (process_handle,0); // for now TODO
	/*
	IMPORT_DESCRIPTOR *rvp = ImportsTable;

	stopAddress = 0;
	if (rvp == NULL || IsBadReadPtr (rvp,4)) {
		TerminateProcess (hProcess,0);
		gbAttached = 0;
		return;
	}
	while (rvp) {
		if (!strnicmp (rvp->DllName,"kernel32.dll",12) &&
			!strncmp (rvp->Name,"ExitProcess",11)) {
			stopAddress = rvp->AddressOfCall;
			break;
		}
		if (!strnicmp (rvp->DllName,"crtdll.dll",10) &&
			!strncmp (rvp->Name,"exit",4)) {
			stopAddress = rvp->AddressOfCall;
			break;
		}
		rvp = rvp->Next;
	}
	if (stopAddress == 0)
	while (exitFunctions [i] && pExitPoint == NULL) {
		pExitPoint = FindGlobalSymbol (exitFunctions [i]);
		if (pExitPoint) {
			stopAddress = pExitPoint->Address;
			break;
		}
		i++;
	}
	i = 0;
	if (stopAddress == 0 && pExitPoint == NULL)
	while (exitFunctions [i] && stopAddress == 0) {
		stopAddress = FindAddressInSymbolTable (exitFunctions [i]);
		i++;
	}
	if (stopAddress == 0) {
		TerminateProcess (hProcess,0);
		gbAttached = 0;
		return;
	}
	if (Debuggee.bInaccessibleFlag) {
		SetAllCodePagesAccess (0);
	}
	*/
	set_step_flag (0);
	// Debuggee.Context.Eip = stopAddress;
    
	if (!g_paused)
		ResumeThread (m_debug_info.debuggee.thread_handle);
	g_paused = FALSE;
	SetEvent (m_command_events [EVENT_RESUME]);
}


/* Start one Thread which will be the designated debugger thread 
	return value > 1 means thread debugger thread was properly started
	return value == 0 means a bug has occured while starting the thread
*/
static int start_debugging (DEBUG_INFO *dbg_info) 
{
	DWORD dwThreadId;
	int i_rval;
	HANDLE dbg_handle;
	/* accessors ? REMIND */
	g_assert (dbg_info &&  dbg_info->server_handle &&  dbg_info->server_handle->arch);
	dbg_handle = dbg_info->debugger_handle;
	if (dbg_handle) {
		/* debugging thread is up just proceed */
		return 1;
	}
	/* have the events been set up properly ? */
	if (! m_command_events [EVENT_RUNNING]) {
		/* not initialized so do it now */
		i_rval = initialize_events ();
		if (! i_rval) {
			return 0;
		}
	}
	/* create a thread to wait for debugging events */
	dbg_handle = CreateThread (NULL, 0,
			 (LPTHREAD_START_ROUTINE)debugging_thread,
			 (LPVOID)dbg_info, 0, &dwThreadId);
	if (! dbg_handle) {
		format_windows_error_message (GetLastError ());
		dbg_info->debugger_handle = dbg_handle;
		return 0;
	}
	assert (dbg_handle);
	set_debugger_handle_and_thread_id (dbg_handle, dwThreadId);
	assert (dbg_info->debugger_handle && dbg_info->debugger_id > 0);
	return 1;

}

static void dummy_proc (char* msg)
{
	g_debug ("%s\n", msg);

}

/* The main debugger event loop is started here */
DWORD debugging_thread (DEBUG_INFO *dbg_info)
{

	int iCmdEvent;
	BOOL b_rval;

	g_attached = NOT_ATTACHED;
	 /* create the debuggee process */
	if (! launch_debuggee ()) {
		format_windows_error_message (GetLastError ());
		/* output ? where */
		
	 } else {
			g_attached = ATTACHED; /* creation succeeded */
	        SetEvent (m_command_events [EVENT_RUNNING]);
	}
	
	while (g_attached == ATTACHED) {
        /* proceed only when a command event permits it */
        iCmdEvent = WaitForMultipleObjects (
			                     G_COMMAND_EVENTS_MAX, (PHANDLE) & m_command_events,
                                 FALSE, INFINITE);
        switch (iCmdEvent) {
        case EVENT_RUNNING:
			check_for_debug_event (m_debug_info.debuggee.process_handle);
			// dummy_proc ("gbAttached debugging_thread placeholder");
            break;
        case EVENT_RESUME:
			SetEvent (m_command_events [EVENT_RUNNING]);
			dummy_proc ("EVENT_RESUME");
            g_paused = FALSE;
			/* ATTENTION may be filled out wrong */
			ContinueDebugEvent (m_debug_info.debuggee.process_id,
				 (DWORD)m_debug_info.debuggee.thread_id, DBG_CONTINUE);
            break;
		case EVENT_RESULT:
			dummy_proc ("EVENT_RESULT");
			break;
		case EVENT_MEMORY_READ:
			b_rval = ResetEvent (m_command_events [EVENT_MEMORY_READ]);
			if (! b_rval) {
				m_info_buf.read_or_written_bytes = 0;
				goto out_read;
			}
			SetLastError (0);
			b_rval = ReadProcessMemory (m_debug_info.debuggee.process_handle, (LPCVOID) m_info_buf.start,
									   (LPVOID) m_info_buf.buffer, m_info_buf.size,
									   &m_info_buf.read_or_written_bytes);
			if (! b_rval || m_info_buf.read_or_written_bytes != m_info_buf.size) {
				format_windows_error_message (GetLastError ());
				
			}
out_read:
			SetEvent (m_command_events [EVENT_RESULT]);
			break;
		case EVENT_MEMORY_WRITE:
			b_rval = ResetEvent (m_command_events [EVENT_MEMORY_WRITE]);
			if (! b_rval) {
				m_info_buf.read_or_written_bytes = 0;
				goto out_write;
			}
			SetLastError (0);
			b_rval = WriteProcessMemory (m_debug_info.debuggee.process_handle, (LPCVOID) m_info_buf.start,
									   (LPVOID) m_info_buf.buffer, m_info_buf.size,
									   &m_info_buf.read_or_written_bytes);
			if (! b_rval || m_info_buf.read_or_written_bytes != m_info_buf.size) {
				format_windows_error_message (GetLastError ());
				
			}
out_write:
			SetEvent (m_command_events [EVENT_RESULT]);
			break;
			
        case EVENT_KILL:
             /* the termination handler cleans up */
             // DoTerminateProcess (pi.hProcess);
			dummy_proc ("EVENT_KILL");
			g_attached = NOT_ATTACHED;
             break;
        }   /* end switch (iCmdEvent) */
	}   /* end while (bAttached) */
	/* clean up */
	ResetEvent (m_command_events [EVENT_RUNNING]);
	// m_debug_info.debuggee.attached = FALSE;
	g_attached = NOT_ATTACHED;
	/* this has to be done but the information needed about the 
	   Debugeee are currenttly unknown to me */

    return (0L);
}

/* Format a more readable error message on failures which set ErrorCode */
static void format_windows_error_message (DWORD error_code)
{
	DWORD dw_rval;
	dw_rval = FormatMessage (FORMAT_MESSAGE_FROM_SYSTEM, NULL,
						   error_code, 0, windows_error_message, 
						   windows_error_message_len, NULL);
	if (FALSE == dw_rval) {
		fprintf (stderr, "Could not get error message from windows\n");
	}
}

static ServerCommandError
server_win32_get_signal_info (ServerHandle *handle, SignalInfo **sinfo_out)
{
	SignalInfo *sinfo = g_new0 (SignalInfo, 1);

	*sinfo_out = sinfo;

	return COMMAND_ERROR_NONE;
}


static void
server_win32_global_init (void)
{
}
static ServerType
server_win32_get_server_type (void)
{
	return SERVER_TYPE_WIN32;
}

static ServerCapabilities
server_win32_get_capabilities (void)
{
	return SERVER_CAPABILITIES_THREAD_EVENTS | SERVER_CAPABILITIES_CAN_DETACH_ANY;
}

struct InferiorHandle
{
	guint32 pid;
	HANDLE process_handle;
	HANDLE thread_handle;
	gint argc;
	gchar **argv;
//#ifdef __linux__
//	int mem_fd;
//#endif
//	int stepping;
//	int last_signal;
	int redirect_fds;
	int output_fd [2], error_fd [2];
	int is_thread, is_initialized;
};

static ServerHandle *
server_win32_create_inferior (BreakpointManager *bpm)
{
	ServerHandle *handle = g_new0 (ServerHandle, 1);

	handle->bpm = bpm;
	handle->inferior = g_new0 (InferiorHandle, 1);
	handle->arch = x86_arch_initialize ();

	return handle;
}

static ServerCommandError
server_win32_get_target_info (guint32            *target_int_size,
							  guint32            *target_long_size,
							  guint32            *target_address_size,
							  guint32            *is_bigendian)
{
	*target_int_size = sizeof (guint32);
	*target_long_size = sizeof (guint32);
	*target_address_size = sizeof (void *);
	*is_bigendian = 0;

	return COMMAND_ERROR_NONE;

}

/* copied from i386-arch.c */
void
x86_arch_remove_breakpoints_from_target_memory (ServerHandle *handle, guint64 start,
						guint32 size, gpointer buffer)
{
	GPtrArray *breakpoints;
	guint8 *ptr = buffer;
	int i;

	mono_debugger_breakpoint_manager_lock ();

	breakpoints = mono_debugger_breakpoint_manager_get_breakpoints (handle->bpm);
	for (i = 0; i < breakpoints->len; i++) {
		BreakpointInfo *info = g_ptr_array_index (breakpoints, i);
		guint32 offset;

		if (info->is_hardware_bpt || !info->enabled)
			continue;
		if ( (info->address < start) || (info->address >= start+size))
			continue;

		offset = (guint32) info->address - start;
		ptr [offset] = info->saved_insn;
	}

	mono_debugger_breakpoint_manager_unlock ();
}



static ServerCommandError
x86_arch_disable_breakpoint (ServerHandle *handle, BreakpointInfo  *breakpoint)
{
	ServerCommandError result;
	ArchInfo *arch = handle->arch;
	InferiorHandle *inferior = handle->inferior;
	guint32 address;

	if (!breakpoint->enabled)
		return COMMAND_ERROR_NONE;

	address = (guint32) breakpoint->address;

	#if 0
	if (breakpoint->dr_index >= 0) {
		X86_DR_DISABLE (arch, breakpoint->dr_index);

		result = _server_ptrace_set_dr (inferior, breakpoint->dr_index, 0L);
		if (result != COMMAND_ERROR_NONE) {
			g_warning (G_STRLOC ": %d", result);
			return result;
		}

		result = _server_ptrace_set_dr (inferior, DR_CONTROL, arch->dr_control);
		if (result != COMMAND_ERROR_NONE) {
			g_warning (G_STRLOC ": %d", result);
			return result;
		}

		arch->dr_regs [breakpoint->dr_index] = 0;
	} else {
#endif
		result = server_win32_write_memory (handle, address, 1, &breakpoint->saved_insn);
		if (result != COMMAND_ERROR_NONE)
			return result;

		/*if (handle->mono_runtime) {
			result = runtime_info_disable_breakpoint (handle, breakpoint);
			if (result != COMMAND_ERROR_NONE)
				return result;

		}
		*/
// 	} from else 

	return COMMAND_ERROR_NONE;
}


static ServerCommandError
server_win32_get_frame (ServerHandle *handle, StackFrame *frame)
{
	ServerCommandError result;
	int i_rval;
	m_debug_info.server_handle = handle;

	i_rval = get_set_registers (&m_debug_info.debuggee.context, READ);
	result = i_rval != 0;
	if (result == 0) {
		return COMMAND_ERROR_INTERNAL_ERROR;
	}

	frame->address = (guint32) INFERIOR_REG_EIP (handle->arch->current_regs);
	frame->stack_pointer = (guint32) INFERIOR_REG_ESP (handle->arch->current_regs);
	frame->frame_address = (guint32) INFERIOR_REG_EBP (handle->arch->current_regs);
	return COMMAND_ERROR_NONE;
}


/* windows version */
static ServerCommandError 
x86_arch_enable_breakpoint (ServerHandle *handle, BreakpointInfo * breakpoint)
{
	ServerCommandError result;
	ArchInfo *arch = handle->arch;
	InferiorHandle *inferior = handle->inferior;
	char bopcode = BP_OPCODE;
	guint32 address;

	if (breakpoint->enabled)
		return COMMAND_ERROR_NONE;

	address = (guint32) breakpoint->address;
/* hardware breakpoint handling */
#if 0
	if (breakpoint->dr_index >= 0) {
		X86_DR_SET_RW_LEN (arch, breakpoint->dr_index, DR_RW_EXECUTE | DR_LEN_1);
		X86_DR_LOCAL_ENABLE (arch, breakpoint->dr_index);

		result = _server_ptrace_set_dr (inferior, breakpoint->dr_index, address);
		if (result != COMMAND_ERROR_NONE) {
			g_warning (G_STRLOC);
			return result;
		}

		result = _server_ptrace_set_dr (inferior, DR_CONTROL, arch->dr_control);
		if (result != COMMAND_ERROR_NONE) {
			g_warning (G_STRLOC);
			return result;
		}

		arch->dr_regs [breakpoint->dr_index] = breakpoint->id;
	} else {

#endif
		/* save away one instruction, which will get replaced by the breakpoint */
		result = server_win32_read_memory (handle, address, 1, &breakpoint->saved_insn);
		if (result != COMMAND_ERROR_NONE)
			return result;

		/* this probably has to be used in managed code */
		/*
		if (handle->mono_runtime) {
			result = runtime_info_enable_breakpoint (handle, breakpoint);
			if (result != COMMAND_ERROR_NONE)
				return result;
		}
		*/
	/* do write the breakpoint opcode */
		result = server_win32_write_memory (handle, address, 1, &bopcode);
		if (result != COMMAND_ERROR_NONE)
			return result;
// 	} commented for now it'f from the if for the hardware breakpoints 

	return COMMAND_ERROR_NONE;

}

static int is_breakline_in_current_function (guint32 eip) 
{
	int i;
	struct function_decriptor_rec *fn;
    
	fn = m_debug_info.debuggee.current_function.function_descriptor;
	if (fn == NULL)
		return (0);
	for (i = 0; i < fn->number_of_breakpoint_lines; i++) {
		if (fn->first_line_offset [i] == eip) {
			/* Show the greatest line number if a series arrives */
			while (i < fn->number_of_breakpoint_lines-1 &&
				fn->first_line_offset [i+1] == eip)
				   i++;
            return (fn->first_line_number [i]);
		}
    }
    return (0);
}


static int current_function_valid (guint32 eip)
{
	struct current_function_rec current_function = m_debug_info.debuggee.current_function;
	if (eip >= current_function.start_address && eip < current_function.end_address)
        return (1);
	if (current_function.function_descriptor == NULL)
		return 0;
	if (eip >= current_function.function_descriptor->address && eip < current_function.start_address)
        return (1);
    return (0);
}
/* just a dummy for now TODO  frido */
int find_current_function_by_address (guint32 eip)
{
	// SetCurrentModule (eip);
    // return SetCurrentFunct
	return 1;
}

static int find_line_in_function (struct function_decriptor_rec *fd,guint32 eip)
{
	//int i,midx;
#if 0
	if (fd == NULL)
		return 0;
	if (fd->FirstLineOffset == NULL) {
		midx = FindModuleByAddress (fd->Address);
		if (midx) {
			BuildLineNumbersForFunction (&Modules [midx], fd);
		}
		else return 0;
	}
	for (i=1; i<fd->NumberOfBPLines;i++) {
		if (fd->FirstLineOffset [i] > eip)
			return fd->FirstLineNumber [i-1];
		else if (eip == fd->FirstLineNumber [i] &&
			i == (fd->NumberOfBPLines-1))
			return fd->FirstLineNumber [i];
	}
#endif
	return (0);
}

/* tricky stuff, it's not yet fully clear how that should be done here */
static int single_step_checks (guint32 eip) 
{
    int i_rval;
	struct current_function_rec current_function = m_debug_info.debuggee.current_function;

    i_rval = is_breakline_in_current_function (eip);
    if (i_rval) {
stopit:
		set_step_flag (0);
		set_stepping (0);
		current_function.trace_flag = current_function.step_flag = 0;
		m_debug_info.debuggee.last_valid_source_line = i_rval;
	    current_function.function_descriptor->last_valid_line = i_rval;

		
		
		/*if (CurrentFunction.Module) {
			ShowCurrentBpLine (bl);
		}
		*/
        return (0);
    }
	if (!current_function_valid (eip)) {
        if (find_current_function_by_address (eip)) {
			set_step_flag (FALSE);
			set_stepping (0);
			current_function.trace_flag = current_function.step_flag = 0;
			m_debug_info.debuggee.last_valid_source_line = find_line_in_function (current_function.function_descriptor, eip);

            return (0);
        }
        else {
			set_all_code_pages_access (1);
			set_step_flag (FALSE);
			set_stepping (0);
			// CurrentFunction.inSingleStep = 0;
			return 2;
		}
    }
	return 1;
	/* TODO implement HandleCall here properly frido */
	// return (HandleCallInstruction (eip));
}

/*----------------------------------------------------
      Set the instruction pointer back one byte.
  ----------------------------------------------------*/
static BOOL decrement_ip (HANDLE debuggee_thread_handle)
{
    BOOL b_rval;
	struct debuggee_info_rec debuggee = m_debug_info.debuggee;
	CONTEXT tc;
	
	debuggee.context.Eip--;
	memcpy (&tc,&debuggee.context,sizeof (CONTEXT));
    tc.ContextFlags = CONTEXT_CONTROL;
	SetLastError (0);
    b_rval = SetThreadContext (debuggee_thread_handle, &tc);
	if (! b_rval){
		format_windows_error_message (GetLastError ());
		g_debug ("%s, could not reset the debuggee to rexecute the INT 3 instruction\n");
	}
    return (b_rval);
}



static BOOL write_opcode (HANDLE debuggee_process_handle, guint32 write_address, PBYTE opcode)
{
    BOOL b_rval; 
    DWORD dw_bytes;
	DWORD dw_new_flags, dw_old_flags;
    /* change mem protection in debuggee for writing */
    b_rval = VirtualProtectEx (debuggee_process_handle, 
							  (PBYTE) write_address,
                                1L, PAGE_READWRITE, &dw_old_flags);
    if (!b_rval ) {
		format_windows_error_message (GetLastError ());
		g_debug ("%s\n", windows_error_message);
        return (FALSE);
    }
	/* write new byte to memory */
	SetLastError (0);
    b_rval = WriteProcessMemory (debuggee_process_handle, 
            (PBYTE) write_address, opcode, 1L, &dw_bytes);
	if (! b_rval) {
		format_windows_error_message (GetLastError ());
		g_debug ("Windows error: %s, while writing %s at %p, opcode could not be written\n", windows_error_message,
			       (char*) opcode, write_address);
	}
	/* now reset the original state of the pages */
	dw_new_flags = dw_old_flags;
	SetLastError (0);
	b_rval = VirtualProtectEx (debuggee_process_handle, (PBYTE) write_address, 1L,
							dw_new_flags, &dw_old_flags);
    
	if (! b_rval){
	   format_windows_error_message (GetLastError ());
	   g_debug ("%s, could not reset page protection\n", windows_error_message);
	}
    return (b_rval);
}



/*    
    Remove a breakpoint instruction from the
    debuggee code.  (Does not remove the breakpont from the hash table of breakpoints 
*/
   
  
	BOOL remove_breakpoint (BreakpointInfo *breakpoint)
{

	guint32 addr_32;
	g_assert (breakpoint->address < ULONG_MAX);
	addr_32 = (guint32) breakpoint->address;
	return write_opcode (m_debug_info.debuggee.process_handle, 
					   addr_32, // breakpoint->address,
					   &breakpoint->saved_insn);
//                               &pBP->Opcode);
}

/*
This function reads the address in the import table where the program will jump.
The address can be either one byte more from the EIP value (when a breakpoint was
set at the import table) or two bytes, when in single stepping we want to set all
code pages to inaccessible.
*/
static int resume_from_imports_table (int flag)
{
	guint32 pdata,data;
	DWORD read_bytes;
	struct debuggee_info_rec debuggee = m_debug_info.debuggee;

    if (flag)
		debuggee.eip++;		
	else
		debuggee.eip += 2;
	/* error handling ? CHECK frido*/
	if (! read_from_debuggee ( (LPVOID)debuggee.context.Eip, (PBYTE) &pdata, 4, &read_bytes))
		return 0;
	if (! read_from_debuggee ( (LPVOID)pdata, (PBYTE)&data, 4,&read_bytes))
		return 0;
	debuggee.context.Eip = data;
    return get_set_registers (&debuggee.context, WRITE);
}

static ServerCommandError
server_win32_get_registers (ServerHandle *handle, guint64 *values) 
{
	return COMMAND_ERROR_NONE;
}

/* official function which fits mdb for fetching the register values 
   ATTENTION. Is only the debug thread allowed to call this then
   we have to introduce another event (jacob)
   on Linux we have two funtions for that. this is the officieal prototyp
   and then we have an internal call to ptrace to really fill the strucutre
   so we have to be aware of that, should we just use this handle or first
   assign it to m_debug_info */

static ServerCommandError
server_win32_set_registers (ServerHandle *handle, guint64 *values) 
{
	ArchInfo *arch = handle->arch;
	int i_rval;
	/* let's store the handle away for us */
	m_debug_info.server_handle = handle;

	/* fill Registers from Values see x86-linux-ptrace.h e.g 
	   must be adjusted to access the proper elements of the CONTEXT structure on Windows */
	/* outcommented because the macros are not yet implemented 
	INFERIOR_REG_EBX (arch->current_regs) = values [DEBUGGER_REG_RBX];
	INFERIOR_REG_ECX (arch->current_regs) = values [DEBUGGER_REG_RCX];
	INFERIOR_REG_EDX (arch->current_regs) = values [DEBUGGER_REG_RDX];
	INFERIOR_REG_ESI (arch->current_regs) = values [DEBUGGER_REG_RSI];
	INFERIOR_REG_EDI (arch->current_regs) = values [DEBUGGER_REG_RDI];
	INFERIOR_REG_EBP (arch->current_regs) = values [DEBUGGER_REG_RBP];
	INFERIOR_REG_EAX (arch->current_regs) = values [DEBUGGER_REG_RAX];
	INFERIOR_REG_DS (arch->current_regs) = values [DEBUGGER_REG_DS];
	INFERIOR_REG_ES (arch->current_regs) = values [DEBUGGER_REG_ES];
	INFERIOR_REG_FS (arch->current_regs) = values [DEBUGGER_REG_FS];
	INFERIOR_REG_GS (arch->current_regs) = values [DEBUGGER_REG_GS];
	INFERIOR_REG_EIP (arch->current_regs) = values [DEBUGGER_REG_RIP];
	INFERIOR_REG_CS (arch->current_regs) = values [DEBUGGER_REG_CS];
	INFERIOR_REG_EFLAGS (arch->current_regs) = values [DEBUGGER_REG_EFLAGS];
	INFERIOR_REG_ESP (arch->current_regs) = values [DEBUGGER_REG_RSP];
	INFERIOR_REG_SS (arch->current_regs) = values [DEBUGGER_REG_SS];
	*/
	/* I think only the debugger thread should be allowed to read this information */
	i_rval = get_set_registers (&arch->current_regs, READ);
	if (! i_rval) 
		/* return  error code, test which one */
		return -1; /* what error code is ok  here? */
	else 
		return COMMAND_ERROR_NONE;
}



static int get_set_registers (CONTEXT *context,  enum RW_FLAG flag)
{
	CONTEXT tc = {0};
	tc.ContextFlags = CONTEXT_CONTROL|CONTEXT_INTEGER|
							CONTEXT_SEGMENTS|CONTEXT_FLOATING_POINT|CONTEXT_EXTENDED_REGISTERS;
	if (READ == flag) {
		if (!GetThreadContext (m_debug_info.debuggee.thread_handle, &tc)) {
			g_debug ("Could not read register information properly\n");
			return 0;
		}
	} else { /* writing */
		if (!SetThreadContext (m_debug_info.debuggee.thread_handle, &tc)) {
			g_debug ("Error while writing into the registers\n");
			return 0;
		}
	}
	return 1;
}

BreakpointInfo * find_breakpoint_by_address (DWORD address) 
	{
		BreakpointManager *bpm = m_debug_info.server_handle->bpm;
		BreakpointInfo *result;
	    g_assert (NULL != bpm);
		result = mono_debugger_breakpoint_manager_lookup (bpm, address);
		return result;
	
	}

static BreakpointInfo* on_breakpoint_exception (DWORD address,int *b_continue)
{
    // Breakpoint pBP;
    // PBPNODE pbpPassed = NULL;
	BreakpointInfo *breakpoint;
	BreakpointInfo *breakpoint_passed = {0};
	int flags,uLine,m, i_rval;
	struct debuggee_info_rec *debuggee = &m_debug_info.debuggee;
    /* The first breakpoint is supplied by */
    /* NT when the program loads */
	*b_continue = FALSE;
	if (0 == debuggee->breakpoint_hit) {
		debuggee->breakpoint_hit = 1;
		// *b_continue = TRUE;
		// ArrangeImportsAddresses ();
#if 0
		if (debuggee->is_attached) {
			// SetupAttachedProcess ();
			*b_continue = TRUE;
		}
		if (debuggee->stripped_exe) {
			*b_continue = TRUE;
		}
#endif
			// SendMessage (hWedit,WM_ESTABLISHBKPTS,0,0);
			// SetForegroundWindow (hWedit);
		
		return (NULL);
	} else if (1 == debuggee->breakpoint_hit) {
		debuggee->breakpoint_hit = 2;
		// SetupSeh ();
//		RedirectSystemCalls ();
	}
	i_rval  = get_set_registers (&debuggee->context, READ);
	if (0 == i_rval) {
		return NULL;
	}
//    UpdateDisplays ();
    /* is this a known breakpoint? */
	breakpoint = find_breakpoint_by_address (address);
    
    /* has the debuggee stopped on a known breakpoint? */
    if (breakpoint) {
		flags = breakpoint->flags;
		uLine = breakpoint->line;
		if (flags & BPFLAG_FNCALL) {
			// SetEvent (EventCallFn); /* where is it used ? */
			return NULL;
		}
		if (flags & BPFLAG_SYSTEMCALL) {
			*b_continue = TRUE;
			resume_from_imports_table (1);
			return NULL;
		}
		// what do do on Mono here */
        // if (! (flags & (BPFLAG_BPBYADDRESS|BPFLAG_STEPOUT|BPFLAG_SETRACE)))
        //    ShowCurrentBpLine (uLine);
        /* get the INT3 opcode out of there */
		remove_breakpoint (breakpoint);
        if (! (flags & BPFLAG_ONCEONLY)) {
            /* For a hard break, turn on single-stepping */
            /* to restore the INT 3 opcode later */
			set_step_flag (TRUE);
			breakpoint_passed = breakpoint;
        } else {
			if (flags & BPFLAG_STEPOUT) {
				set_step_flag (TRUE);
				set_stepping (1);
				// debuggee->current_function in_single_step = 1;
				*b_continue = TRUE;
			}
			mono_debugger_breakpoint_manager_remove (m_debug_info.server_handle->bpm, breakpoint);
		}
		
		/* now in fact we are behind the INT 3, we have replaced this INT 3 with the original instruction
		  so we have to get before it, beeing able to execute it now */
		decrement_ip (debuggee->thread_handle);
		  
		if (flags & BPFLAG_SKIPCALL) {
#if 0 
			/* how should this be handled in mono? */
			if (Debuggee.FunctionReturnIndex < MAXFNPERLINE) {
				Debuggee.FunctionReturnValues [Debuggee.FunctionReturnIndex] =
					Debuggee.Context.Eax;
				if (ReadProcessBytes (Debuggee.Context.Eip-4,4, (PBYTE)&fnAddr)) {
					Debuggee.FunctionAddresses [Debuggee.FunctionReturnIndex] =
						fnAddr+Debuggee.Context.Eip;
				}
				else
					Debuggee.FunctionAddresses [Debuggee.FunctionReturnIndex] = 0;
				Debuggee.FunctionReturnIndex++;
				CurrentFunction.inSingleStep = 1;
			}
#endif
			set_step_flag (TRUE);
			if (single_step_checks (debuggee->context.Eip))
				*b_continue = TRUE;
		} else if (flags & BPFLAG_HASCOUNT && breakpoint->count > 0) {
			breakpoint->count--;
			*b_continue = TRUE;
		} else if (*b_continue == FALSE) {
			/* CHECK */
			// int midx;

#if 0
			midx = FindModuleByAddress (pXAddress);
			if (midx == 0) {
				if (Debuggee.StrippedExe)
					PostMessage (hWedit,WM_COMMAND,CMD_DISASSEMBLY,0);
				else {
					midx = FindCurrentFunctionInStack (flags & BPFLAG_IMPORT ? 0 : 1);
				}
			}
			if (midx && midx != CurrentModule->Index)
				CurrentModule = &Modules [midx];
			if (FindCurrentFunctionByAddress (pXAddress)) {
				if (CurrentFunction.Function) {
					CurrentFunction.Function->lastValidLine = uLine;
				}
			}
			UpdateDisplays ();
#endif
		} else {  /* unknown breakpoint */
			

			if (debuggee->created_new_process) {
				*b_continue = TRUE;
				debuggee->created_new_process = 0;
				return breakpoint_passed;
			}
			g_debug ("Breakpoint found in code at address: %p\n", address);
		
			m = find_current_function_by_address (address);
			if (m) {
			// sprintf (tmpbuf+strlen (tmpbuf)," + Module %d: %s\n",m,Modules [m].Name);
				; 
			} else {
				;
			}
		} /* unkown breakpoint */
			
#if 0
			LOADED_DLL_INFO *infoDll;
			infoDll = FindDllByAddress (pXAddress);
			if (infoDll) {
				char *fnName=NULL;
				int nextop=0;
				sprintf (tmpbuf+strlen (tmpbuf)," + Dll: %s\n", infoDll->Name);
				if (LookupInImportTable (pXAddress,&fnName)) {
					sprintf (tmpbuf+strlen (tmpbuf)," + function %s\n",fnName);
				}
				else {
					ReadProcessBytes (Debuggee.Context.Eip,1, (PBYTE)&nextop);
					if (nextop == 0xC3 || nextop == 0xC2) {
						ReadProcessBytes (Debuggee.Context.Esp,4, (PBYTE)&nextop);
						sprintf (tmpbuf+strlen (tmpbuf)," + called from 0x%x\n",nextop);
					}
				}

			}
		}
		InfoMsg (tmpbuf);
		DoDisplayEvent (tmpbuf);
		release (tmpbuf);
		;
		// FindCurrentFunctionInStack (1);
		}
#endif
    /* Return pointer to the breakpoint if it must */
    /* be restored after the next single-step exception */
	}
	{
	int status = BREAKPOINT_HIT_DEBUG_EVENT;
	add_to_bridge (status);
	}
    return (breakpoint_passed);
}




static ServerCommandError
server_win32_insert_breakpoint (ServerHandle *handle, guint64 address, guint32 *bhandle)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup (handle->bpm, address);
	if (breakpoint) {
		/*
		 * You cannot have a hardware breakpoint and a normal breakpoint on the same
		 * instruction.
		 */
		if (breakpoint->is_hardware_bpt) {
			mono_debugger_breakpoint_manager_unlock ();
			return COMMAND_ERROR_DR_OCCUPIED;
		}

		breakpoint->refcount++;
		goto done;
	}

	breakpoint = g_new0 (BreakpointInfo, 1);

	breakpoint->refcount = 1;
	breakpoint->address = address;
	breakpoint->is_hardware_bpt = FALSE;
	breakpoint->id = mono_debugger_breakpoint_manager_get_next_id ();
	breakpoint->dr_index = -1;

	result = x86_arch_enable_breakpoint (handle, breakpoint);
	if (result != COMMAND_ERROR_NONE) {
		mono_debugger_breakpoint_manager_unlock ();
		g_free (breakpoint);
		return result;
	}

	breakpoint->enabled = TRUE;
	mono_debugger_breakpoint_manager_insert (handle->bpm, (BreakpointInfo *) breakpoint);
 done:
	*bhandle = breakpoint->id;
	mono_debugger_breakpoint_manager_unlock ();

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
server_win32_remove_breakpoint (ServerHandle *handle, guint32 bhandle)
{
	BreakpointInfo *breakpoint;
	ServerCommandError result;

	mono_debugger_breakpoint_manager_lock ();
	breakpoint = (BreakpointInfo *) mono_debugger_breakpoint_manager_lookup_by_id (handle->bpm, bhandle);
	if (!breakpoint) {
		result = COMMAND_ERROR_NO_SUCH_BREAKPOINT;
		goto out;
	}

	if (--breakpoint->refcount > 0) {
		result = COMMAND_ERROR_NONE;
		goto out;
	}

	result = x86_arch_disable_breakpoint (handle, breakpoint);
	if (result != COMMAND_ERROR_NONE)
		goto out;

	breakpoint->enabled = FALSE;
	mono_debugger_breakpoint_manager_remove (handle->bpm, (BreakpointInfo *) breakpoint);

 out:
	mono_debugger_breakpoint_manager_unlock ();
	return result;
}

static int launch_debuggee (void)
{
	STARTUPINFO si = {0};
    BOOL b_ret;
	int result = 1;
	
	SECURITY_ATTRIBUTES sa;
	SECURITY_DESCRIPTOR sd;
	PROCESS_INFORMATION pi = {0};
	LPSECURITY_ATTRIBUTES lpsa=NULL;
	int Flags = FALSE;
	InferiorHandle *inferior = m_debug_info.server_handle->inferior;

	g_assert (m_debug_info.server_handle != NULL);
	g_assert (m_debug_info.w_working_directory != NULL);
	si.cb = sizeof (STARTUPINFO);
	
    /* fill in the process's startup information
	   we have to check how this startup info have to be filled and how 
	   redirection should be setup, this is an REMINDER for doing that */

	fwprintf (stderr, L"Spawning process with\nCommand line: %s\nWorking Directory: %s\nThread Id: %d\n", 
		m_debug_info.w_argv_flattened, m_debug_info.w_working_directory, GetCurrentThreadId ());

	InitializeSecurityDescriptor (&sd,SECURITY_DESCRIPTOR_REVISION);
	SetSecurityDescriptorDacl (&sd,TRUE,NULL,FALSE);
	sa.nLength = sizeof (SECURITY_ATTRIBUTES);
	sa.bInheritHandle = TRUE;
	sa.lpSecurityDescriptor = &sd;
	lpsa = &sa;

	b_ret = CreateProcess (NULL, m_debug_info.w_argv_flattened,
                             lpsa, lpsa, FALSE, 
							 DEBUG_PROCESS | DEBUG_ONLY_THIS_PROCESS|CREATE_NEW_PROCESS_GROUP|CREATE_UNICODE_ENVIRONMENT, 
							 m_debug_info.w_envp_flattened,
							 m_debug_info.w_working_directory, &si, &pi);
	
	if (!b_ret)
	{
		/* cleanup code where to place here or one function above REMIND */
		format_windows_error_message (GetLastError ());
	    /* this should find it's way to the proper glib error mesage handler */
		fwprintf (stderr, windows_error_message);
		return 0;
	}



	// *child_pid = pi.dwProcessId;
	/* keep some information in our own Debug info structure for now I suppose this information
	has to be merged into the inferior structure of the ServerHandle */
	set_debuggee_process_information (&pi);
	/* unchanged from what Jonatan has written 
	this information may be used on he CSharp side of the code. REMIND */
	inferior->pid = m_debug_info.debuggee.process_id; 
	inferior->process_handle = m_debug_info.debuggee.process_handle;
	inferior->thread_handle = m_debug_info.debuggee.thread_handle;
	SetEvent (m_command_events [EVENT_RESULT]);
	return 1;
}

static ServerCommandError
server_win32_spawn (ServerHandle *handle, const gchar *working_directory,
		     const gchar **argv, const gchar **envp, gboolean redirect_fds,
			 gint *child_pid, IOThreadData **io_data, gchar **error)
{	
	
	int i_ret;

	gunichar2* utf16_argv = NULL;
	gunichar2* utf16_envp = NULL;
	gunichar2* utf16_working_directory = NULL;
	InferiorHandle *inferior;
	g_assert (handle != NULL);
	
	
	inferior = handle->inferior;
	g_assert (inferior != NULL);

	
	if (working_directory) {
		utf16_working_directory = g_utf8_to_utf16 (working_directory, -1, NULL, NULL, NULL);
	}

	if (envp) {
		guint len = 0;
		const gchar** envp_temp = envp;
		gunichar2* envp_concat;

		while (*envp_temp) {
			len += strlen (*envp_temp) + 1;
			envp_temp++;
		}
		len++; /* add one for double NULL at end */
		envp_concat = utf16_envp = g_malloc (len*sizeof (gunichar2));

		envp_temp = envp;
		while (*envp_temp) {
			gunichar2* utf16_envp_temp = g_utf8_to_utf16 (*envp_temp, -1, NULL, NULL, NULL);
			int written = swprintf (envp_concat, len, L"%s%s", utf16_envp_temp, L"\0");
			g_free (utf16_envp_temp);
			envp_concat += written + 1;
			len -= written;
			envp_temp++;
		}
		swprintf (envp_concat, len, L"%s", L"\0"); /* double NULL at end */
	}

	if (argv) {
		gint argc = 0;
		guint len = 0;
		gint index = 0;
		const gchar** argv_temp = argv;
		gunichar2* argv_concat;

		while (*argv_temp) {
			len += strlen (*argv_temp) + 1;
			argv_temp++;
			argc++;
		}
		inferior->argc = argc;
		inferior->argv = g_malloc0 ( (argc+1) * sizeof (gpointer));
		argv_concat = utf16_argv = g_malloc (len*sizeof (gunichar2));

		argv_temp = argv;
		while (*argv_temp) {
			gunichar2* utf16_argv_temp = g_utf8_to_utf16 (*argv_temp, -1, NULL, NULL, NULL);
			int written = swprintf (argv_concat, len, L"%s ", utf16_argv_temp);
			inferior->argv [index++] = g_strdup (*argv_temp);
			g_free (utf16_argv_temp);
			argv_concat += written;
			len -= written;
			argv_temp++;
		}
	}

	/* accessors ? REMIND */
	/* keep a copy of the first parameter internally */
	m_debug_info.server_handle = handle;
	m_debug_info.w_working_directory = utf16_working_directory;
	m_debug_info.w_argv_flattened =  utf16_argv;
	m_debug_info.w_envp_flattened = utf16_envp;
	m_debug_info.argv = argv;
	m_debug_info.envp = envp;
	m_debug_info.redireckt_fds = redirect_fds;

	ResetEvent (m_command_events [EVENT_RESULT]);


	/* get the debugger thread up and running */
	i_ret = start_debugging (&m_debug_info);
	if (! i_ret) {
		return COMMAND_ERROR_NO_TARGET;
	}
	WaitForSingleObject (m_command_events [EVENT_RESULT], INFINITE);
	*child_pid = m_debug_info.debuggee.process_id;


	return COMMAND_ERROR_NONE;
}

void
server_win32_io_thread_main (IOThreadData *io_data, ChildOutputFunc func)
{
	Sleep (600000);
}

static guint32
server_win32_global_wait (guint32 *status_ret)
{
	int ret;
	BOOL b_rval;
	GTimeVal *queue_timeout;

	guint val;
	void *val_data;
	CRITICAL_SECTION cs;
	DWORD dw_rval = 0;
	g_assert (GetCurrentThread () != m_debug_info.debugger_handle);
	

    m_windows_event_code = -1;
	if (! bridge) {
		bridge = g_async_queue_new ();
	}
	
	val_data = g_async_queue_pop (bridge);
	val = GPOINTER_TO_INT (val_data);
	*status_ret = val;

	/* correct return value`? */
	return m_debug_info.debuggee.process_id;
}





static ServerCommandError
server_win32_initialize_process (ServerHandle *handle)
{
	return COMMAND_ERROR_NONE;
}



static ServerStatusMessageType
server_win32_dispatch_simple (guint32 status, guint32 *arg)
{
	if (status >> 16)
		return MESSAGE_UNKNOWN_ERROR;
#if 0
	if (WIFSTOPPED (status)) {
		int stopsig = WSTOPSIG (status);

		if ( (stopsig == SIGSTOP) || (stopsig == SIGTRAP))
			stopsig = 0;

		*arg = stopsig;
		return MESSAGE_CHILD_STOPPED;
	} else if (WIFEXITED (status)) {
		*arg = WEXITSTATUS (status);
		return MESSAGE_CHILD_EXITED;
	} else if (WIFSIGNALED (status)) {
		if ( (WTERMSIG (status) == SIGTRAP) || (WTERMSIG (status) == SIGKILL)) {
			*arg = 0;
			return MESSAGE_CHILD_EXITED;
		} else {
			*arg = WTERMSIG (status);
			return MESSAGE_CHILD_SIGNALED;
		}
	}
#endif

	return MESSAGE_UNKNOWN_ERROR;
}


static ServerStatusMessageType
server_win32_dispatch_event (ServerHandle *handle, guint32 status, guint64 *arg,
			      guint64 *data1, guint64 *data2, guint32 *opt_data_size,
			      gpointer *opt_data)

{
	switch (status) {
		case CREATE_PROCESS_DEBUG_EVENT:
			return MESSAGE_CHILD_EXECD;
		case CREATE_THREAD_DEBUG_EVENT:
			return MESSAGE_CHILD_CREATED_THREAD;
		case EXIT_PROCESS_DEBUG_EVENT:
			return MESSAGE_CHILD_CALLED_EXIT;
		default:
			return MESSAGE_UNKNOWN_ERROR;
				
	}
}

void GetProcessStrings (HANDLE hProcess, LPWSTR lpszCmdLine, LPWSTR lpszEnvVars);

static ServerCommandError
server_win32_get_application (ServerHandle *handle, gchar **exe_file, gchar **cwd,
			       guint32 *nargs, gchar ***cmdline_args)
{
	gint index = 0;
	GPtrArray *array;
	gchar **ptr;
	/* No supported way to get command line of a process
	   see http://blogs.msdn.com/oldnewthing/archive/2009/02/23/9440784.aspx */

/*	gunichar2 utf16_exe_file [1024];
	gunichar2 utf16_cmd_line [10240];
	gunichar2 utf16_env_vars [10240];
	BOOL ret;
	if (!GetModuleFileNameEx (handle->inferior->process_handle, NULL, utf16_exe_file, sizeof (utf16_exe_file)/sizeof (utf16_exe_file [0]))) {
		DWORD error = GetLastError ();
		return COMMAND_ERROR_INTERNAL_ERROR;
	}
	*/
	*exe_file = g_strdup (handle->inferior->argv [0]);
	*nargs = handle->inferior->argc;

	array = g_ptr_array_new ();

	for (index = 0; index < handle->inferior->argc; index++)
		g_ptr_array_add (array, handle->inferior->argv [index]);

	*cmdline_args = ptr = g_new0 (gchar *, array->len + 1);

	for (index = 0; index < array->len; index++)
		ptr  [index] = g_ptr_array_index (array, index);

	g_ptr_array_free (array, FALSE);

	return COMMAND_ERROR_NONE;
}

static guint32
server_win32_get_current_pid (void)
{
	return GetCurrentProcessId ();
}

static guint64
server_win32_get_current_thread (void)
{
	return GetCurrentThreadId ();
}


static ServerCommandError 
read_write_memory (ServerHandle *server_handle, guint64 start, guint32 size, gpointer buffer, int read) 
{
	BOOL b_rval;
	CRITICAL_SECTION cs;
	DWORD dw_rval = 0;
	g_assert (GetCurrentThread () != m_debug_info.debugger_handle);	
	InitializeCriticalSection (&cs);
	EnterCriticalSection (&cs);
	m_debug_info.server_handle = server_handle;
	m_info_buf.start = start;
	m_info_buf.size = size;
	m_info_buf.buffer = buffer;
	SetLastError (0);
	b_rval = ResetEvent (m_command_events [EVENT_RESULT]);
	if (! b_rval) {
		format_windows_error_message (GetLastError ());
		return COMMAND_ERROR_MEMORY_ACCESS;
	}
	SetLastError (0);
	b_rval = SetEvent (m_command_events [EVENT_MEMORY_READ]);
	if (! b_rval) {
		format_windows_error_message (GetLastError ());
		return COMMAND_ERROR_MEMORY_ACCESS;

	}
	SetLastError (0);
	dw_rval = WaitForSingleObject (m_command_events [EVENT_RESULT], 5000);  
	if (dw_rval == WAIT_FAILED) {
		format_windows_error_message (GetLastError ());
		return COMMAND_ERROR_MEMORY_ACCESS;
	}
	if (! m_info_buf.read_or_written_bytes) {
		if (read) {
			buffer = NULL;
		}
		return COMMAND_ERROR_MEMORY_ACCESS;
	} else {
		if (read) {
			buffer = m_info_buf.buffer;
		}
	}
	LeaveCriticalSection (&cs);

	return COMMAND_ERROR_NONE;

}

static BOOL read_from_debuggee (LPVOID start, LPVOID buf, DWORD size, PDWORD read_bytes) 
{
	BOOL b_rval = FALSE;
	g_assert (GetCurrentThreadId ()== m_debug_info.debugger_id);
	SetLastError (0);
	b_rval = ReadProcessMemory (m_debug_info.debuggee.process_handle, start, buf, size, read_bytes);
	if (! b_rval) {
		format_windows_error_message (GetLastError ());
		g_debug ("%s", windows_error_message);
	}
	return b_rval;
}

static ServerCommandError
server_win32_get_breakpoints (ServerHandle *handle, guint32 *count, guint32 **retval)
{
	int i;
	GPtrArray *breakpoints;

	mono_debugger_breakpoint_manager_lock ();
	breakpoints = mono_debugger_breakpoint_manager_get_breakpoints (handle->bpm);
	*count = breakpoints->len;
	*retval = g_new0 (guint32, breakpoints->len);

	for (i = 0; i < breakpoints->len; i++) {
		BreakpointInfo *info = g_ptr_array_index (breakpoints, i);

		 (*retval) [i] = info->id;
	}
	mono_debugger_breakpoint_manager_unlock ();

	return COMMAND_ERROR_NONE;	
}


/* memory access functions */
static ServerCommandError 
server_win32_read_memory (ServerHandle *server_handle, guint64 start, guint32 size, gpointer buffer) 
{
	return read_write_memory (server_handle, start, size, buffer, 1);
}

static ServerCommandError 
server_win32_write_memory (ServerHandle *server_handle, guint64 start, guint32 size, gconstpointer buffer) 
{
	return read_write_memory (server_handle, start, size, (gpointer)buffer, 0);
}

InferiorVTable i386_windows_inferior = {
	server_win32_global_init,			/*global_init, */
	server_win32_get_server_type,
	server_win32_get_capabilities,
	server_win32_create_inferior,		/*create_inferior, */
	server_win32_initialize_process,	/*initialize_process, */
	NULL,					 			/*initialize_thread, */
	NULL,					 			/*set_runtime_info, */
	server_win32_io_thread_main,		/*io_thread_main, */
	server_win32_spawn,					/*spawn, */
	NULL,		 						/*attach, */
	NULL,					 			/*detach, */
	NULL,					 			/*finalize, */
	server_win32_global_wait,			/*global_wait, */
	NULL,					 			/*stop_and_wait, */
	server_win32_dispatch_event,		/*dispatch_event, */
	server_win32_dispatch_simple,								/*dispatch_simple, */
	server_win32_get_target_info,		/*get_target_info, */
	NULL,					 			/*continue, */
	NULL,					 			/*step, */
	NULL,					 			/*resume, */
	server_win32_get_frame,	 			/*get_frame, */
	NULL,					 			/*current_insn_is_bpt, */
	NULL,					 			/*peek_word, */
	server_win32_read_memory,			/*read_memory, */
	server_win32_write_memory,		/*write_memory, */
	NULL,					 			/*call_method, */
	NULL,					 			/*call_method_1, */
	NULL,					 			/*call_method_2, */
	NULL,					 			/*call_method_3, */
	NULL,					 			/*call_method_invoke, */
	NULL,					 			/*execute_instruction, */
	NULL,					 			/*mark_rti_frame, */
	NULL,					 			/*abort_invoke, */
	server_win32_insert_breakpoint,					 			/*insert_breakpoint, */
	NULL,					 			/*insert_hw_breakpoint, */
	server_win32_remove_breakpoint,					 			/*remove_breakpoint, */
	NULL,					 			/*enable_breakpoint, */
	NULL,					 			/*disable_breakpoint, */
	server_win32_get_breakpoints,		/*get_breakpoints, */
	server_win32_get_registers,					 			/*get_registers, */
	server_win32_set_registers,					 			/*set_registers, */
	NULL,					 			/*stop, */
	NULL,					 			/*set_signal, */
	NULL,					 			/*server_ptrace_get_pending_signal, */
	NULL,					 			/*kill, */
	server_win32_get_signal_info,					 			/*get_signal_info, */
	NULL,					 			/*get_threads, */
	server_win32_get_application,		/*get_application, */
	NULL,								/*detach_after_fork, */
	NULL,								/*push_registers, */
	NULL,								/*pop_registers, */
	NULL,								/*get_callback_frame, */
	NULL,								/*server_ptrace_restart_notification, */
	NULL,								/*get_registers_from_core_file, */
	server_win32_get_current_pid,		/*get_current_pid, */
	server_win32_get_current_thread		/*get_current_thread, */
	};


