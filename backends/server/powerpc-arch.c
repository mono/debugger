#define _GNU_SOURCE
#include <server.h>
#include <breakpoints.h>
#include <sys/stat.h>
#include <sys/ptrace.h>
#include <sys/socket.h>
#include <sys/wait.h>
#include <signal.h>
#include <unistd.h>
#include <string.h>
#include <fcntl.h>
#include <errno.h>

#include "powerpc-arch.h"

struct ArchInfo
{
	INFERIOR_REGS_TYPE current_regs;
	INFERIOR_REGS_TYPE *saved_regs;
};

ArchInfo *
powerpc_arch_initialize (void)
{
	ArchInfo *arch = g_new0 (ArchInfo, 1);

	return arch;
}

void
powerpc_arch_finalize (ArchInfo *arch)
{
	g_free (arch->saved_regs);
	g_free (arch);
}

static ServerCommandError
powerpc_arch_get_registers (ServerHandle *handle)
{
	ServerCommandError result;

	result = _powerpc_get_registers (handle->inferior, &handle->arch->current_regs);
	if (result != COMMAND_ERROR_NONE)
		return result;

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
powerpc_get_registers (ServerHandle *handle, guint32 count,
		       guint32 *registers, guint64 *values)
{
	ArchInfo *arch = handle->arch;
	int i;

	for (i = 0; i < count; i++) {
		switch (registers [i]) {
		default:
			return COMMAND_ERROR_UNKNOWN_REGISTER;
		}
	}

	return COMMAND_ERROR_NONE;
}

static ServerCommandError
powerpc_get_pc (ServerHandle *handle, guint64 *pc)
{
	ppc_thread_state_t regs = handle->arch->current_regs;
	guint32 ret;

	g_message (G_STRLOC ": %p - %p", handle, handle->arch->current_regs);

	ret = (guint32) INFERIOR_REG_EIP (handle->arch->current_regs);
	g_message (G_STRLOC ": %x,%x,%x,%x", regs.srr0, regs.srr1, regs.r0, regs.r1);
	*pc = ret;
	return COMMAND_ERROR_NONE;
}

static ServerCommandError
powerpc_get_ret_address (ServerHandle *handle, guint64 *retval)
{
	*retval = 0;
	return COMMAND_ERROR_NONE;
}

