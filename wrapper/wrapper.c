#include <mono-debugger-jit-wrapper.h>
#include <mono/io-layer/io-layer.h>
#include <mono/metadata/verify.h>
#include <mono/metadata/threads.h>
#include <mono/metadata/mono-debug.h>
#define IN_MONO_DEBUGGER
#include <mono/private/libgc-mono-debugger.h>
#include <semaphore.h>
#include <unistd.h>
#include <locale.h>

static sem_t main_started_cond;
static sem_t command_started_cond;
static sem_t command_ready_cond;
static sem_t main_ready_cond;

#define HEAP_SIZE 1048576
static char mono_debugger_heap [HEAP_SIZE];

static MonoMethod *debugger_main_method;

static guint64 debugger_insert_breakpoint (guint64 method_argument, const gchar *string_argument);
static guint64 debugger_remove_breakpoint (guint64 breakpoint);
static guint64 debugger_compile_method (MonoMethod *method);
static guint64 debugger_create_string (guint64 dummy_argument, const gchar *string_argument);
static guint64 debugger_class_get_static_field_data (guint64 klass);
static guint64 debugger_lookup_type (guint64 dummy_argument, const gchar *string_argument);

static void (*command_notification_function) (void);
void (*mono_debugger_notification_function) (int command, gpointer data, guint32 data2);

/*
 * This is a global data symbol which is read by the debugger.
 */
MonoDebuggerInfo MONO_DEBUGGER__debugger_info = {
	MONO_DEBUGGER_MAGIC,
	MONO_DEBUGGER_VERSION,
	sizeof (MonoDebuggerInfo),
	&mono_generic_trampoline_code,
	&mono_debugger_symbol_table,
	sizeof (MonoDebuggerSymbolTable),
	&debugger_compile_method,
	&debugger_insert_breakpoint,
	&debugger_remove_breakpoint,
	&mono_debugger_runtime_invoke,
	&debugger_create_string,
	&debugger_class_get_static_field_data,
	&debugger_lookup_type,
	mono_debugger_heap,
	HEAP_SIZE
};

MonoDebuggerManager MONO_DEBUGGER__manager = {
	sizeof (MonoDebuggerManager),
	NULL, NULL, 0, 0, NULL, NULL, NULL
};

void
mono_debugger_wait_cond (sem_t *cond)
{
	sem_wait (cond);
}

static guint64
debugger_insert_breakpoint (guint64 method_argument, const gchar *string_argument)
{
	MonoMethodDesc *desc;

	desc = mono_method_desc_new (string_argument, FALSE);
	if (!desc)
		return 0;

	return (guint64) mono_debugger_insert_breakpoint_full (desc);
}

static guint64
debugger_remove_breakpoint (guint64 breakpoint)
{
	return mono_debugger_remove_breakpoint (breakpoint);
}

static guint64
debugger_compile_method (MonoMethod *method)
{
	gpointer retval;

	mono_debugger_lock ();
	retval = mono_compile_method (method);
	mono_debugger_unlock ();

	mono_debugger_notification_function (NOTIFICATION_METHOD_COMPILED, retval, 0);

	return GPOINTER_TO_UINT (retval);
}

static guint64
debugger_create_string (guint64 dummy_argument, const gchar *string_argument)
{
	return GPOINTER_TO_UINT (mono_string_new_wrapper (string_argument));
}

static guint64
debugger_lookup_type (guint64 dummy_argument, const gchar *string_argument)
{
	guint64 retval;

	mono_debugger_lock ();
	retval = mono_debugger_lookup_type (string_argument);
	mono_debugger_unlock ();
	return retval;
}

static guint64
debugger_class_get_static_field_data (guint64 value)
{
	MonoClass *klass = GUINT_TO_POINTER ((guint32) value);
	MonoVTable *vtable = mono_class_vtable (mono_domain_get (), klass);
	return GPOINTER_TO_UINT (vtable->data);
}

static void
debugger_event_handler (MonoDebuggerEvent event, gpointer data, guint32 arg)
{
	switch (event) {
	case MONO_DEBUGGER_EVENT_RELOAD_SYMTABS:
		mono_debugger_notification_function (NOTIFICATION_RELOAD_SYMTABS, NULL, 0);
		break;

	case MONO_DEBUGGER_EVENT_BREAKPOINT:
		mono_debugger_notification_function (NOTIFICATION_JIT_BREAKPOINT, data, arg);
		break;
	}
}

static MonoThreadCallbacks thread_callbacks = {
	&debugger_compile_method,
	&mono_debugger_thread_manager_add_thread,
	&mono_debugger_thread_manager_start_resume,
	&mono_debugger_thread_manager_end_resume
};

static void
initialize_debugger_support (void)
{
	sem_init (&main_started_cond, 0, 0);
	sem_init (&command_started_cond, 0, 0);
	sem_init (&command_ready_cond, 0, 0);
	sem_init (&main_ready_cond, 0, 0);

	command_notification_function = mono_debugger_create_notification_function
		(&MONO_DEBUGGER__manager.command_notification);
	mono_debugger_notification_function = mono_debugger_create_notification_function
		(&MONO_DEBUGGER__manager.notification_address);
}

typedef struct 
{
	MonoDomain *domain;
	const char *file;
} DebuggerThreadArgs;

typedef struct
{
	MonoDomain *domain;
	MonoMethod *method;
	int argc;
	char **argv;
} MainThreadArgs;

static guint32
main_thread_handler (gpointer user_data)
{
	MainThreadArgs *main_args = (MainThreadArgs *) user_data;
	int retval;

	MONO_DEBUGGER__manager.main_tid = IO_LAYER (GetCurrentThreadId) ();
	MONO_DEBUGGER__manager.main_thread = g_new0 (MonoDebuggerThread, 1);
	MONO_DEBUGGER__manager.main_thread->tid = IO_LAYER (GetCurrentThreadId) ();
	MONO_DEBUGGER__manager.main_thread->start_stack = &main_args;

	mono_debugger_thread_manager_thread_created (MONO_DEBUGGER__manager.main_thread);

	sem_post (&main_started_cond);

	/*
	 * Wait until everything is ready.
	 */
	mono_debugger_wait_cond (&main_ready_cond);

	retval = mono_runtime_run_main (main_args->method, main_args->argc, main_args->argv, NULL);

	return retval;
}

static guint32
command_thread_handler (gpointer user_data)
{
	MONO_DEBUGGER__manager.command_tid = IO_LAYER (GetCurrentThreadId) ();
	sem_post (&command_started_cond);

	/*
	 * Wait until everything is ready.
	 */
	mono_debugger_wait_cond (&command_ready_cond);

	/*
	 * This call will never return.
	 */
	command_notification_function ();

	return 0;
}

void
MONO_DEBUGGER__start_main (void)
{
	/*
	 * Reload symbol tables.
	 */
	mono_debugger_notification_function (NOTIFICATION_INITIALIZE_MANAGED_CODE, NULL, 0);
	mono_debugger_notification_function (NOTIFICATION_INITIALIZE_THREAD_MANAGER, NULL, 0);

	mono_debugger_unlock ();

	/*
	 * Signal the main thread that it can execute the managed Main().
	 */
	sem_post (&main_ready_cond);
	sem_post (&command_ready_cond);

	// mono_debugger_thread_manager_main ();

	mono_thread_manage ();
}

int
mono_debugger_main (MonoDomain *domain, const char *file, int argc, char **argv, char **envp)
{
	MainThreadArgs main_args;
	MonoAssembly *assembly;

	initialize_debugger_support ();

	mono_debugger_init_icalls ();

	/*
	 * Create the command thread and wait until it's ready.
	 */
	mono_thread_create (domain, command_thread_handler, NULL);
	mono_debugger_wait_cond (&command_started_cond);

	/*
	 * Start the debugger thread and wait until it's ready.
	 */
	mono_debug_init (domain, MONO_DEBUG_FORMAT_DEBUGGER);

	assembly = mono_domain_assembly_open (domain, file);
	if (!assembly){
		fprintf (stderr, "Can not open image %s\n", file);
		exit (1);
	}

	mono_debug_init_2 (assembly);

	/*
	 * Get and compile the main function.
	 */

	debugger_main_method = mono_get_method (
		assembly->image, mono_image_get_entry_point (assembly->image), NULL);
	MONO_DEBUGGER__manager.main_function = mono_compile_method (debugger_main_method);

	/*
	 * Start the main thread and wait until it's ready.
	 */

	main_args.domain = domain;
	main_args.method = debugger_main_method;
	main_args.argc = argc - 2;
	main_args.argv = argv + 2;

	mono_thread_create (domain, main_thread_handler, &main_args);
	mono_debugger_wait_cond (&main_started_cond);

	/*
	 * Initialize the thread manager.
	 */

	mono_debugger_event_handler = debugger_event_handler;
	mono_install_thread_callbacks (&thread_callbacks);
	mono_debugger_thread_manager_init ();

	MONO_DEBUGGER__start_main ();

	return 0;
}
