#include <stdio.h>
#include <stdlib.h>
#include <dlfcn.h>

int
main (int argc, const char *argv [])
{
	void *handle;
	void (*ptr) (char *, int);

	if (argc != 2) {
		fprintf (stderr, "USAGE: %s filename\n", argv [0]);
		exit (1);
	}

	handle = dlopen (argv [1], RTLD_NOW | RTLD_GLOBAL);
	fprintf (stderr, "LOAD: %s - %p\n", argv [1], handle);
	fflush (stderr);
	if (!handle) {
		fprintf (stderr, "ERROR: %s\n", dlerror ());
		exit (0);
	}
	ptr = dlsym (handle, "hello");
	fprintf (stderr, "PTR: %p\n", ptr);
	ptr ("Hello from module", 9);
	exit (0);
}
