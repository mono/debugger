#include <mono-debugger-jit-wrapper.h>
#include <mono/jit/jit.h>
#include <locale.h>

extern gboolean mono_break_on_exc;

extern MonoDomain *
mono_init_debugger (const char *file, const char *opt_flags);

int
main (int argc, char **argv, char **envp)
{
	MonoDomain *domain;
	const char *file;
	int retval;

	setlocale(LC_ALL, "");
	g_log_set_always_fatal (G_LOG_LEVEL_ERROR);
	g_log_set_fatal_mask (G_LOG_DOMAIN, G_LOG_LEVEL_ERROR);

	g_assert (argc >= 3);
	file = argv [2];

	mono_break_on_exc = TRUE;

	domain = mono_init_debugger (file, argv [1]);

	retval = mono_debugger_main (domain, file, argc, argv, envp);

	mono_jit_cleanup (domain);

	return retval;
}
