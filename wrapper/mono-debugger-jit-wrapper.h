#ifndef __MONO_DEBUGGER_JIT_WRAPPER_H
#define __MONO_DEBUGGER_JIT_WRAPPER_H 1

#include <mono/metadata/mono-debug-debugger.h>
#include <semaphore.h>

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
	gconstpointer notification_address;
	MonoDebuggerSymbolTable **symbol_table;
	guint32 symbol_table_size;
	gpointer (*compile_method) (MonoMethod *method);
	guint64 (*insert_breakpoint) (guint64 method_argument, const gchar *string_argument);
	guint64 (*remove_breakpoint) (guint64 breakpoint);
	MonoInvokeFunc runtime_invoke;
	guint64 (*create_string) (guint64 dummy_argument, const gchar *string_argument);
	gpointer *event_data;
	guint32 *event_arg;
	gpointer heap;
	guint32 heap_size;
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

void mono_debugger_wait_cond (sem_t *cond);
void mono_debugger_thread_manager_init (void);
void mono_debugger_thread_manager_main (void);
void mono_debugger_thread_manager_add_thread (guint32 thread, gpointer stack_start, gpointer func);
void mono_debugger_thread_manager_thread_created (MonoDebuggerThread *thread);
void mono_debugger_thread_manager_start_resume (guint32 thread);
void mono_debugger_thread_manager_end_resume (guint32 thread);
void mono_debugger_thread_manager_acquire_global_thread_lock (void);
void mono_debugger_thread_manager_release_global_thread_lock (void);
void mono_debugger_init_icalls (void);

void MONO_DEBUGGER__main (void);

extern void (*mono_debugger_thread_manager_notification_function) (gpointer func);
extern gpointer MONO_DEBUGGER__thread_manager_notification;
extern gpointer MONO_DEBUGGER__command_notification;
extern int MONO_DEBUGGER__main_pid;
extern MonoDebuggerThread *MONO_DEBUGGER__main_thread;
extern int MONO_DEBUGGER__debugger_thread;
extern int MONO_DEBUGGER__command_thread;
extern int MONO_DEBUGGER__thread_manager_last_pid;
extern MonoMethod *MONO_DEBUGGER__main_method;
extern gpointer MONO_DEBUGGER__main_function;
extern gpointer MONO_DEBUGGER__thread_manager_last_func;
extern guint32 MONO_DEBUGGER__thread_manager_last_thread;

G_END_DECLS

#endif
