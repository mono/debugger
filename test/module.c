#include <stdio.h>
#include <stdlib.h>
#include <dlfcn.h>

int
main (void)
{
	void *handle;
	void (*ptr) (char *, int);

	handle = dlopen ("/home/martin/monocvs/debugger/test/libfoo.so", RTLD_NOW | RTLD_GLOBAL);
	fprintf (stderr, "LOAD: %p\n", handle);
	if (!handle) {
		fprintf (stderr, "ERROR: %s\n", dlerror ());
		exit (0);
	}
	ptr = dlsym (handle, "hello");
	fprintf (stderr, "PTR: %p\n", ptr);
	ptr ("Hello from module", 9);
	exit (0);
}
