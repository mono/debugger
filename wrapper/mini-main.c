#include <mono-debugger-jit-wrapper.h>
#include <mono/metadata/verify.h>
#include <locale.h>

int
main (int argc, char **argv, char **envp)
{
	MonoDomain *domain;
	const char *file, *error;
	int retval;

	setlocale(LC_ALL, "");
	g_log_set_always_fatal (G_LOG_LEVEL_ERROR);
	g_log_set_fatal_mask (G_LOG_DOMAIN, G_LOG_LEVEL_ERROR);

	g_assert (argc >= 3);
	file = argv [2];

	g_set_prgname (file);

	mono_parse_default_optimizations (argv [1]);

	domain = mono_jit_init (argv [0]);

	mono_config_parse (NULL);

	error = mono_verify_corlib ();
	if (error) {
		fprintf (stderr, "Corlib not in sync with this runtime: %s\n", error);
		exit (1);
	}

	retval = mono_debugger_main (domain, file, argc, argv, envp);

	mono_jit_cleanup (domain);

	return retval;
}
