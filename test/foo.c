#include <stdio.h>
#include <stdlib.h>

void
hello (const char *message, int a)
{
	printf ("%s - %d\n", message, getpid ());
	fflush (stdout);
}
