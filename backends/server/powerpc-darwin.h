#ifndef __MONO_DEBUGGER_POWERPC_H__
#define __MONO_DEBUGGER_POWERPC_H__

#include <mach/mach_init.h>
#include <mach/mach_host.h>
#include <mach/thread_status.h>
#include <mach/thread_act.h>
#include <mach/mach_traps.h>
#include <mach/mach_error.h>
#include <mach/vm_map.h>

ServerCommandError
_powerpc_get_registers (InferiorHandle *inferior, INFERIOR_REGS_TYPE *regs);

static void
_powerpc_setup_inferior (ServerHandle *handle, gboolean is_main);

#endif
