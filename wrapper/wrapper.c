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

static sem_t debugger_thread_cond;
static sem_t debugger_started_cond;
static sem_t main_started_cond;
static sem_t command_started_cond;
static sem_t command_ready_cond;
static sem_t main_ready_cond;

static sem_t debugger_finished_cond;

static gboolean debugger_signalled = FALSE;
static gboolean must_send_finished = FALSE;

#define HEAP_SIZE 1048576
static char mono_debugger_heap [HEAP_SIZE];

static MonoMethod *debugger_main_method;
static gpointer debugger_event_data;
static guint32 debugger_event_arg;

static guint64 debugger_insert_breakpoint (guint64 method_argument, const gchar *string_argument);
static guint64 debugger_remove_breakpoint (guint64 breakpoint);
static gpointer debugger_compile_method (MonoMethod *method);
static guint64 debugger_create_string (guint64 dummy_argument, const gchar *string_argument);
static guint64 debugger_class_get_static_field_data (guint64 klass);
static guint64 debugger_lookup_type (guint64 dummy_argument, const gchar *string_argument);

static gpointer debugger_notification_address;
static void (*debugger_notification_function) (void);
static void (*command_notification_function) (void);

/*
 * This is a global data symbol which is read by the debugger.
 */
MonoDebuggerInfo MONO_DEBUGGER__debugger_info = {
	MONO_DEBUGGER_MAGIC,
	MONO_DEBUGGER_VERSION,
	sizeof (MonoDebuggerInfo),
	&mono_generic_trampoline_code,
	&debugger_notification_address,
	&mono_debugger_symbol_table,
	sizeof (MonoDebuggerSymbolTable),
	&debugger_compile_method,
	&debugger_insert_breakpoint,
	&debugger_remove_breakpoint,
	&mono_debugger_runtime_invoke,
	&debugger_create_string,
	&debugger_class_get_static_field_data,
	&debugger_lookup_type,
	&debugger_event_data,
	&debugger_event_arg,
	mono_debugger_heap,
	HEAP_SIZE
};

MonoDebuggerManager MONO_DEBUGGER__manager = {
	sizeof (MonoDebuggerManager),
	NULL, NULL, NULL, NULL,
	&mono_debugger_thread_manager_notify_command,
	&mono_debugger_thread_manager_notify_tid
};

void
mono_debugger_wait_cond (sem_t *cond)
{
	sem_wait (cond);
}

static void
mono_debugger_wait (void)
{
	mono_debugger_wait_cond (&debugger_finished_cond);
}

static void
mono_debugger_signal (void)
{
	mono_debugger_lock ();
	if (!debugger_signalled) {
		debugger_signalled = TRUE;
		sem_post (&debugger_thread_cond);
	}
	mono_debugger_unlock ();
}

static guint64
debugger_insert_breakpoint (guint64 method_argument, const gchar *string_argument)
{
	MonoMethodDesc *desc;

	desc = mono_method_desc_new (string_argument, FALSE);
	if (!desc)
		return 0;

	return mono_debugger_insert_breakpoint_full (desc);
}

static guint64
debugger_remove_breakpoint (guint64 breakpoint)
{
	return mono_debugger_remove_breakpoint (breakpoint);
}

static gpointer
debugger_compile_method (MonoMethod *method)
{
	gpointer retval;

	mono_debugger_lock ();
	must_send_finished = TRUE;
	retval = mono_compile_method (method);
	mono_debugger_signal ();
	mono_debugger_unlock ();

	mono_debugger_wait ();

	return retval;
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
	mono_debugger_signal ();
	mono_debugger_unlock ();
	return retval;
}

static guint64
debugger_class_get_static_field_data (guint64 klass)
{
	MonoVTable *vtable = mono_class_vtable (mono_domain_get (), GINT_TO_POINTER (klass));
	return GPOINTER_TO_UINT (vtable->data);
}

static void
debugger_event_handler (MonoDebuggerEvent event, gpointer data, guint32 arg)
{
	switch (event) {
	case MONO_DEBUGGER_EVENT_TYPE_ADDED:
	case MONO_DEBUGGER_EVENT_METHOD_ADDED:
		mono_debugger_signal ();
		break;

	case MONO_DEBUGGER_EVENT_BREAKPOINT:
		mono_debugger_lock ();
		must_send_finished = TRUE;
		debugger_event_data = data;
		debugger_event_arg = arg;
		mono_debugger_signal ();
		mono_debugger_unlock ();

		mono_debugger_wait ();
		break;

	default:
		g_assert_not_reached ();
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
	sem_init (&debugger_thread_cond, 0, 0);
	sem_init (&debugger_started_cond, 0, 0);
	sem_init (&main_started_cond, 0, 0);
	sem_init (&command_started_cond, 0, 0);
	sem_init (&command_ready_cond, 0, 0);
	sem_init (&main_ready_cond, 0, 0);

	sem_init (&debugger_finished_cond, 0, 0);

	debugger_notification_function = mono_debugger_create_notification_function
		(&debugger_notification_address);
	command_notification_function = mono_debugger_create_notification_function
		(&MONO_DEBUGGER__manager.command_notification);
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
debugger_thread_handler (gpointer user_data)
{
	DebuggerThreadArgs *debugger_args = (DebuggerThreadArgs *) user_data;
	MonoAssembly *assembly;
	MonoImage *image;

	mono_debug_init (debugger_args->domain, MONO_DEBUG_FORMAT_DEBUGGER);

	assembly = mono_domain_assembly_open (debugger_args->domain, debugger_args->file);
	if (!assembly){
		fprintf (stderr, "Can not open image %s\n", debugger_args->file);
		exit (1);
	}

	mono_debug_init_2 (assembly);

	/*
	 * Get and compile the main function.
	 */

	image = assembly->image;
	debugger_main_method = mono_get_method (image, mono_image_get_entry_point (image), NULL);
	MONO_DEBUGGER__manager.main_function = mono_compile_method (debugger_main_method);

	/*
	 * Signal the main thread that we're ready.
	 */
	sem_post (&debugger_started_cond);

	/*
	 * This mutex is locked by the parent thread until the debugger actually
	 * attached to us, so we don't need a SIGSTOP here anymore.
	 */
	mono_debugger_lock ();

	while (TRUE) {
		/* Wait for an event. */
		mono_debugger_unlock ();
		mono_debugger_wait_cond (&debugger_thread_cond);
		mono_debugger_lock ();

		/*
		 * Send notification - we'll stop on a breakpoint instruction at a special
		 * address.  The debugger will reload the symbol tables while we're stopped -
		 * and owning the `debugger_thread_mutex' so that no other thread can touch
		 * them in the meantime.
		 */
		debugger_notification_function ();

		debugger_signalled = FALSE;
		debugger_event_data = NULL;
		debugger_event_arg = 0;

		if (must_send_finished) {
			sem_post (&debugger_finished_cond);
			must_send_finished = FALSE;
		}
	}

	return 0;
}

static guint32
main_thread_handler (gpointer user_data)
{
	MainThreadArgs *main_args = (MainThreadArgs *) user_data;
	int retval;

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
	sem_post (&command_started_cond);

	/*
	 * Wait until everything is ready.
	 */
	mono_debugger_wait_cond (&command_ready_cond);

	/*
	 * This call will never return.
	 */
	debugger_notification_function ();

	return 0;
}

void
MONO_DEBUGGER__start_main (void)
{
	/*
	 * Reload symbol tables.
	 */
	must_send_finished = TRUE;
	mono_debugger_signal ();
	mono_debugger_unlock ();

	mono_debugger_wait ();

	/*
	 * Signal the main thread that it can execute the managed Main().
	 */
	sem_post (&main_ready_cond);
	sem_post (&command_ready_cond);

	mono_debugger_thread_manager_main ();

	mono_thread_manage ();
}

int
mono_debugger_main (MonoDomain *domain, const char *file, int argc, char **argv, char **envp)
{
	DebuggerThreadArgs debugger_args;
	MainThreadArgs main_args;

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
	debugger_args.domain = domain;
	debugger_args.file = file;

	mono_thread_create (domain, debugger_thread_handler, &debugger_args);
	mono_debugger_wait_cond (&debugger_started_cond);

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
