#include <mono-runtime-info.h>

MonoRuntimeInfo *
mono_debugger_server_initialize_mono_runtime (guint64 notification_address,
					      guint64 executable_code_buffer,
					      guint32 executable_code_buffer_size,
					      guint64 breakpoint_table,
					      guint32 breakpoint_table_size)
{
	MonoRuntimeInfo *runtime = g_new0 (MonoRuntimeInfo, 1);

	runtime->notification_address = notification_address;
	runtime->executable_code_buffer = executable_code_buffer;
	runtime->executable_code_buffer_size = executable_code_buffer_size;
	runtime->breakpoint_table = breakpoint_table;
	runtime->breakpoint_table_size = breakpoint_table_size;

	return runtime;
}
