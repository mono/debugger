#ifndef __MONO_DEBUGGER_JIT_WRAPPER_H
#define __MONO_DEBUGGER_JIT_WRAPPER_H 1

#include <mono/jit/debug.h>

G_BEGIN_DECLS

typedef struct _MonoDebuggerInfo		MonoDebuggerInfo;
typedef struct _MonoDebuggerThread		MonoDebuggerThread;

/*
 * There's a global data symbol called `MONO_DEBUGGER__debugger_info' which
 * contains pointers to global variables and functions which must be accessed
 * by the debugger.
 */
struct _MonoDebuggerInfo {
	guint64 magic;
	guint32 version;
	guint32 total_size;
	guint8 **generic_trampoline_code;
	guint8 **breakpoint_trampoline_code;
	guint32 *symbol_file_generation;
	guint32 *symbol_file_modified;
	gconstpointer notification_address;
	MonoDebuggerSymbolFileTable **symbol_file_table;
	gpointer (*compile_method) (MonoMethod *method);
	guint64 (*insert_breakpoint) (guint64 method_argument, const gchar *string_argument);
	guint64 (*remove_breakpoint) (guint64 breakpoint);
	MonoInvokeFunc runtime_invoke;
};

/*
 * Thread structure.
 */
struct _MonoDebuggerThread {
	gpointer end_stack;
	guint32 tid, pid;
	guint32 locked;
	gpointer func;
	gpointer start_stack;
};

enum {
	THREAD_MANAGER_CREATE_THREAD,
	THREAD_MANAGER_RESUME_THREAD,
	THREAD_MANAGER_ACQUIRE_GLOBAL_LOCK,
	THREAD_MANAGER_RELEASE_GLOBAL_LOCK
};

#define IO_LAYER(func) (* mono_debugger_io_layer.func)

int mono_debugger_main (MonoDomain *domain, const char *file, int argc, char **argv, char **envp);

void mono_debugger_wait_cond (gpointer cond);
void mono_debugger_thread_manager_init (void);
void mono_debugger_thread_manager_main (void);
void mono_debugger_thread_manager_add_thread (guint32 thread, gpointer stack_start, gpointer func);
void mono_debugger_thread_manager_thread_created (MonoDebuggerThread *thread);
void mono_debugger_thread_manager_start_resume (guint32 thread);
void mono_debugger_thread_manager_end_resume (guint32 thread);
void mono_debugger_thread_manager_acquire_global_thread_lock (void);
void mono_debugger_thread_manager_release_global_thread_lock (void);
void mono_debugger_init_icalls (void);

volatile void MONO_DEBUGGER__main (void);

extern volatile void (*mono_debugger_thread_manager_notification_function) (gpointer func);
extern volatile gpointer MONO_DEBUGGER__thread_manager_notification;
extern volatile gpointer MONO_DEBUGGER__command_notification;
extern volatile int MONO_DEBUGGER__main_pid;
extern volatile MonoDebuggerThread *MONO_DEBUGGER__main_thread;
extern volatile int MONO_DEBUGGER__debugger_thread;
extern volatile int MONO_DEBUGGER__command_thread;
extern volatile int MONO_DEBUGGER__thread_manager_last_pid;
extern volatile MonoMethod *MONO_DEBUGGER__main_method;
extern volatile gpointer MONO_DEBUGGER__main_function;
extern volatile gpointer MONO_DEBUGGER__thread_manager_last_func;
extern volatile guint32 MONO_DEBUGGER__thread_manager_last_thread;

G_END_DECLS

#endif
