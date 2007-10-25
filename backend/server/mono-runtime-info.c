#include <mono-runtime-info.h>

struct _MonoRuntimeInfoPriv {
	guint8 *breakpoint_table_bitfield;
};

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

	runtime->_priv = g_new0 (MonoRuntimeInfoPriv, 1);

	runtime->_priv->breakpoint_table_bitfield = g_malloc0 (breakpoint_table_size);

	return runtime;
}

static int
find_breakpoint_slot (MonoRuntimeInfo *runtime)
{
	int i;

	for (i = 0; i < runtime->breakpoint_table_size; i++) {
		if (runtime->_priv->breakpoint_table_bitfield [i])
			continue;

		runtime->_priv->breakpoint_table_bitfield [i] = 1;
		return i;
	}

	return -1;
}

ServerCommandError
mono_debugger_runtime_info_enable_breakpoint (ServerHandle *handle, guint64 address,
					      guint8 saved_insn)
{
	ServerCommandError result;
	guint64 table_address;
	guint64 header;
	int slot;

	slot = find_breakpoint_slot (handle->mono_runtime);
	if (slot < 0)
		return COMMAND_ERROR_INTERNAL_ERROR;

	g_message (G_STRLOC ": allocated slot %d for breakpoint %Lx / %x", slot, address,
		   saved_insn);

	table_address = handle->mono_runtime->breakpoint_table + 16 * slot;
	g_message (G_STRLOC ": table address is %Lx", table_address);

	header = 1 + (saved_insn << 1);
	result = server_ptrace_write_memory (handle, table_address + 8, 8, &address);
	if (result != COMMAND_ERROR_NONE)
		return result;

	g_message (G_STRLOC);

	result = server_ptrace_write_memory (handle, table_address, 8, &header);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}
