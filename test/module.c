#include <stdio.h>
#include <stdlib.h>
#include <dlfcn.h>

void
crashing_here (int *ptr)
{
	*ptr = 4;
}

int
main (void)
{
	void *handle, *ptr;

	handle = dlopen ("/lib/libm.so.6", RTLD_NOW | RTLD_GLOBAL);
	fprintf (stderr, "LOAD: %p\n", handle);
	if (!handle) {
		fprintf (stderr, "ERROR: %s\n", dlerror ());
		exit (0);
	}
	ptr = dlsym (handle, "sin");
	fprintf (stderr, "PTR: %p\n", ptr);
	crashing_here (NULL);
	exit (0);
}
