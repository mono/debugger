#include <mono-debugger-jit-wrapper.h>
#include <mono/jit/jit.h>
#include <mono/jit/debug.h>
#include <mono/io-layer/io-layer.h>
#include <mono/metadata/verify.h>
#include <mono/metadata/threads.h>
#include <unistd.h>
#include <locale.h>

static gpointer debugger_thread_cond;
static gpointer debugger_started_cond;
static gpointer main_started_cond;
static gpointer command_started_cond;
static gpointer command_ready_cond;
static gpointer main_ready_cond;

static gpointer debugger_finished_cond;

static gboolean debugger_signalled = FALSE;
static gboolean must_send_finished = FALSE;

volatile MonoMethod *MONO_DEBUGGER__main_method = NULL;
volatile gpointer MONO_DEBUGGER__main_function = NULL;
volatile int MONO_DEBUGGER__main_thread = 0;
volatile int MONO_DEBUGGER__debugger_thread = 0;
volatile int MONO_DEBUGGER__command_thread = 0;
volatile gpointer MONO_DEBUGGER__command_notification = NULL;

static guint64 debugger_insert_breakpoint (guint64 method_argument, const gchar *string_argument);
static guint64 debugger_remove_breakpoint (guint64 breakpoint);
static gpointer debugger_compile_method (MonoMethod *method);

static gpointer debugger_notification_address;
static void (*debugger_notification_function) (void);
static void (*command_notification_function) (void);

/*
 * This is a global data symbol which is read by the debugger.
 */
MonoDebuggerInfo MONO_DEBUGGER__debugger_info = {
	MONO_SYMBOL_FILE_DYNAMIC_MAGIC,
	MONO_SYMBOL_FILE_DYNAMIC_VERSION,
	sizeof (MonoDebuggerInfo),
	&mono_generic_trampoline_code,
	&mono_breakpoint_trampoline_code,
	&mono_debugger_symbol_file_table_generation,
	&mono_debugger_symbol_file_table_modified,
	&debugger_notification_address,
	&mono_debugger_symbol_file_table,
	&debugger_compile_method,
	&debugger_insert_breakpoint,
	&debugger_remove_breakpoint,
	&mono_runtime_invoke
};

void
mono_debugger_wait_cond (gpointer cond)
{
	g_assert (IO_LAYER (WaitForSingleObject) (cond, INFINITE) == WAIT_OBJECT_0);
}

static void
mono_debugger_wait (void)
{
	mono_debugger_wait_cond (debugger_finished_cond);
}

static void
mono_debugger_signal (gboolean modified)
{
	if (modified)
		mono_debugger_symbol_file_table_modified = TRUE;
	mono_debug_lock ();
	if (!debugger_signalled) {
		debugger_signalled = TRUE;
		IO_LAYER (ReleaseSemaphore) (debugger_thread_cond, 1, NULL);
	}
	mono_debug_unlock ();
}

static guint64
debugger_insert_breakpoint (guint64 method_argument, const gchar *string_argument)
{
	MonoMethodDesc *desc;

	desc = mono_method_desc_new (string_argument, FALSE);
	if (!desc)
		return 0;

	return mono_insert_breakpoint_full (desc, TRUE);
}

static guint64
debugger_remove_breakpoint (guint64 breakpoint)
{
	return mono_remove_breakpoint (breakpoint);
}

static gpointer
debugger_compile_method (MonoMethod *method)
{
	gpointer retval;

	mono_debug_lock ();
	retval = mono_compile_method (method);
	mono_debugger_signal (FALSE);
	mono_debug_unlock ();
	return retval;
}

static void
debugger_event_handler (MonoDebuggerEvent event, gpointer data, gpointer data2)
{
	switch (event) {
	case MONO_DEBUGGER_EVENT_TYPE_ADDED:
	case MONO_DEBUGGER_EVENT_METHOD_ADDED:
		mono_debugger_signal (TRUE);
		break;

	case MONO_DEBUGGER_EVENT_BREAKPOINT_TRAMPOLINE:
		mono_debug_lock ();
		must_send_finished = TRUE;
		mono_debugger_signal (TRUE);
		mono_debug_unlock ();

		mono_debugger_wait ();
		break;

	case MONO_DEBUGGER_EVENT_THREAD_CREATED:
		mono_debugger_thread_manager_add_thread ((guint32) data, getpid (), data2);
		break;

	default:
		g_assert_not_reached ();
	}
}

static MonoThreadCallbacks thread_callbacks = {
	&debugger_compile_method,
	&mono_debugger_thread_manager_start_resume,
	&mono_debugger_thread_manager_end_resume
};

static void
initialize_debugger_support (void)
{
	debugger_thread_cond = IO_LAYER (CreateSemaphore) (NULL, 0, 1, NULL);
	debugger_started_cond = IO_LAYER (CreateSemaphore) (NULL, 0, 1, NULL);
	main_started_cond = IO_LAYER (CreateSemaphore) (NULL, 0, 1, NULL);
	command_started_cond = IO_LAYER (CreateSemaphore) (NULL, 0, 1, NULL);
	command_ready_cond = IO_LAYER (CreateSemaphore) (NULL, 0, 1, NULL);
	main_ready_cond = IO_LAYER (CreateSemaphore) (NULL, 0, 1, NULL);

	debugger_finished_cond = IO_LAYER (CreateSemaphore) (NULL, 0, 1, NULL);

	debugger_notification_function = mono_debug_create_notification_function
		(&debugger_notification_address);
	command_notification_function = mono_debug_create_notification_function
		(&MONO_DEBUGGER__command_notification);

	mono_debug_init (TRUE);
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
	MonoDebugHandle *debug;
	int last_generation = 0;

	MONO_DEBUGGER__debugger_thread = getpid ();

	assembly = mono_domain_assembly_open (debugger_args->domain, debugger_args->file);
	if (!assembly){
		fprintf (stderr, "Can not open image %s\n", debugger_args->file);
		exit (1);
	}

	mono_debug_format = MONO_DEBUG_FORMAT_MONO;
	debug = mono_debug_open (assembly, mono_debug_format, NULL);

	/*
	 * Get and compile the main function.
	 */

	image = assembly->image;
	MONO_DEBUGGER__main_method = mono_get_method (image, mono_image_get_entry_point (image), NULL);
	MONO_DEBUGGER__main_function = mono_compile_method ((gpointer) MONO_DEBUGGER__main_method);

	/*
	 * Signal the main thread that we're ready.
	 */
	IO_LAYER (ReleaseSemaphore) (debugger_started_cond, 1, NULL);

	/*
	 * This mutex is locked by the parent thread until the debugger actually
	 * attached to us, so we don't need a SIGSTOP here anymore.
	 */
	mono_debug_lock ();

	while (TRUE) {
		/* Wait for an event. */
		mono_debug_unlock ();
		mono_debugger_wait_cond (debugger_thread_cond);
		mono_debug_lock ();

		/* Reload the symbol file table if necessary. */
		if (mono_debugger_symbol_file_table_generation > last_generation) {
			mono_debug_update_symbol_file_table ();
			last_generation = mono_debugger_symbol_file_table_generation;
		}

		/*
		 * Send notification - we'll stop on a breakpoint instruction at a special
		 * address.  The debugger will reload the symbol tables while we're stopped -
		 * and owning the `debugger_thread_mutex' so that no other thread can touch
		 * them in the meantime.
		 */
		debugger_notification_function ();

		/* Clear modified and signalled flag. */
		mono_debugger_symbol_file_table_modified = FALSE;
		debugger_signalled = FALSE;

		if (must_send_finished) {
			IO_LAYER (ReleaseSemaphore) (debugger_finished_cond, 1, NULL);
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

	MONO_DEBUGGER__main_thread = getpid ();
	IO_LAYER (ReleaseSemaphore) (main_started_cond, 1, NULL);

	/*
	 * Wait until everything is ready.
	 */
	mono_debugger_wait_cond (main_ready_cond);

	retval = mono_runtime_run_main (main_args->method, main_args->argc, main_args->argv, NULL);

	return retval;
}

static guint32
command_thread_handler (gpointer user_data)
{
	MONO_DEBUGGER__command_thread = getpid ();
	IO_LAYER (ReleaseSemaphore) (command_started_cond, 1, NULL);

	/*
	 * Wait until everything is ready.
	 */
	mono_debugger_wait_cond (command_ready_cond);

	/*
	 * This call will never return.
	 */
	debugger_notification_function ();

	return 0;
}

int
mono_debugger_main (MonoDomain *domain, const char *file, int argc, char **argv, char **envp)
{
	DebuggerThreadArgs debugger_args;
	MainThreadArgs main_args;

	initialize_debugger_support ();

	debugger_args.domain = domain;
	debugger_args.file = file;

	/*
	 * Start the debugger thread and wait until it's ready.
	 */
	mono_thread_create (domain, debugger_thread_handler, &debugger_args);
	mono_debugger_wait_cond (debugger_started_cond);

	/*
	 * Start the main thread and wait until it's ready.
	 */

	main_args.domain = domain;
	main_args.method = MONO_DEBUGGER__main_method;
	main_args.argc = argc - 1;
	main_args.argv = argv + 1;

	mono_thread_create (domain, main_thread_handler, &main_args);
	mono_debugger_wait_cond (main_started_cond);

	/*
	 * Create the command thread and wait until it's ready.
	 */

	mono_thread_create (domain, command_thread_handler, NULL);
	mono_debugger_wait_cond (command_started_cond);

	/*
	 * Initialize the thread manager.
	 */

	mono_debugger_thread_manager_init ();
	mono_debugger_event_handler = debugger_event_handler;
	mono_install_thread_callbacks (&thread_callbacks);

	/*
	 * Reload symbol tables.
	 */
	must_send_finished = TRUE;
	mono_debugger_signal (TRUE);
	mono_debug_unlock ();

	mono_debugger_wait ();

	/*
	 * Signal the main thread that it can execute the managed Main().
	 */
	IO_LAYER (ReleaseSemaphore) (main_ready_cond, 1, NULL);
	IO_LAYER (ReleaseSemaphore) (command_ready_cond, 1, NULL);

	mono_debugger_thread_manager_main ();

	mono_thread_manage ();

	return 0;
}
