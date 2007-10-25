#include <mono-runtime-info.h>

struct _MonoRuntimeInfoPriv {
	guint8 *breakpoint_table_bitfield;
};

typedef struct {
	/* This is intentionally a bitfield to allow the debugger to write
	 * both `enabled' and `opcode' in one single atomic operation. */
	guint64 enabled	  : 1;
	guint64 opcode    : 8;
	guint64 unused    : 55;
	guint64 address;
} MonoDebuggerBreakpointInfo;

MonoRuntimeInfo *
mono_debugger_server_initialize_mono_runtime (guint32 address_size,
					      guint64 notification_address,
					      guint64 executable_code_buffer,
					      guint32 executable_code_buffer_size,
					      guint64 breakpoint_info_area,
					      guint64 breakpoint_table,
					      guint32 breakpoint_table_size)
{
	MonoRuntimeInfo *runtime = g_new0 (MonoRuntimeInfo, 1);

	runtime->address_size = address_size;
	runtime->notification_address = notification_address;
	runtime->executable_code_buffer = executable_code_buffer;
	runtime->executable_code_buffer_size = executable_code_buffer_size;
	runtime->breakpoint_info_area = breakpoint_info_area;
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
mono_debugger_runtime_info_enable_breakpoint (ServerHandle *handle, BreakpointInfo *breakpoint)
{
	MonoRuntimeInfo *runtime;
	ServerCommandError result;
	guint64 table_address, index_address, header;
	MonoDebuggerBreakpointInfo info;
	int slot;

	runtime = handle->mono_runtime;
	g_assert (runtime);

	slot = find_breakpoint_slot (runtime);
	if (slot < 0)
		return COMMAND_ERROR_INTERNAL_ERROR;

	breakpoint->runtime_table_slot = slot;

	g_message (G_STRLOC ": allocated slot %d for breakpoint %Lx / %x", slot,
		   breakpoint->address, (guint8) breakpoint->saved_insn);

	table_address = runtime->breakpoint_info_area + 16 * slot;
	index_address = runtime->breakpoint_table + runtime->address_size * slot;

	g_message (G_STRLOC ": table address is %Lx / index address is %Lx",
		   table_address, index_address);

	memset (&info, 0, sizeof (MonoDebuggerBreakpointInfo));
	info.enabled = 1;
	info.opcode = breakpoint->saved_insn;
	info.address = breakpoint->address;

	result = server_ptrace_write_memory (handle, table_address, 16, &info);
	if (result != COMMAND_ERROR_NONE)
		return result;

	result = server_ptrace_poke_word (handle, index_address, (gsize) table_address);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}

ServerCommandError
mono_debugger_runtime_info_disable_breakpoint (ServerHandle *handle, BreakpointInfo *breakpoint)
{
	MonoRuntimeInfo *runtime;
	ServerCommandError result;
	guint64 index_address;
	int slot;

	runtime = handle->mono_runtime;
	g_assert (runtime);

	g_message (G_STRLOC ": freeing breakpoint slot %d", breakpoint->runtime_table_slot);

	slot = breakpoint->runtime_table_slot;
	index_address = runtime->breakpoint_table + runtime->address_size * slot;

	g_message (G_STRLOC ": index address is %Lx", index_address);

	result = server_ptrace_poke_word (handle, index_address, 0);
	if (result != COMMAND_ERROR_NONE)
		return result;

	runtime->_priv->breakpoint_table_bitfield [slot] = 0;

	return COMMAND_ERROR_NONE;
}
