#include <mono-debugger-jit-wrapper.h>

static void
test_icall (void)
{
	g_message (G_STRLOC);
}

void
mono_debugger_init_icalls (void)
{
	mono_add_internal_call ("Mono.Debugger.Tests.TestInternCall::Test", test_icall);
	mono_add_internal_call ("Mono.Debugger.Callbacks.ThreadManager::AcquireGlobalThreadLock",
				mono_debugger_thread_manager_acquire_global_thread_lock);
	mono_add_internal_call ("Mono.Debugger.Callbacks.ThreadManager::ReleaseGlobalThreadLock",
				mono_debugger_thread_manager_release_global_thread_lock);
}
