#include <stdio.h>
#include <stdlib.h>
#include <sys/types.h>
#include <unistd.h>

void
crashing_here (int *ptr)
{
	*ptr = 4;
}

void
hello (const char *message, int a)
{
	printf ("%s - %d\n", message, getpid ());
	crashing_here (NULL);
}

int
main (void)
{
	int a = 5;
	hello ("Hello World", a);
	return 0;
}
