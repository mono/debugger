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
}
