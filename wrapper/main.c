#include <mono-debugger-jit-wrapper.h>
#include <mono/metadata/verify.h>
#include <locale.h>

int
main (int argc, char **argv, char **envp)
{
	MonoDomain *domain;
	const char *file, *error;

	setlocale(LC_ALL, "");
	g_log_set_always_fatal (G_LOG_LEVEL_ERROR);
	g_log_set_fatal_mask (G_LOG_DOMAIN, G_LOG_LEVEL_ERROR);

	g_assert (argc >= 3);
	file = argv [2];

	g_set_prgname (file);

	domain = mono_jit_init (file);

	error = mono_verify_corlib ();
	if (error) {
		fprintf (stderr, "Corlib not in sync with this runtime: %s\n", error);
		exit (1);
	}

	return mono_debugger_main (domain, file, argc, argv, envp);
}
