#include <stdio.h>
#include <stdlib.h>
#include <sys/types.h>
#include <unistd.h>

void
hello (const char *message, int a)
{
	printf ("%s - %d\n", message, getpid ());
	fflush (stdout);
}
