#include <stdio.h>
#include <stdlib.h>
#include <signal.h>

void
crashing_here (int *ptr)
{
	*ptr = 4;
}

void
hello (const char *message, int a)
{
	printf ("%s\n", message);
	sleep (5);
	crashing_here (NULL);
}

int
main (void)
{
	int a = 5;
	hello ("Hello World", a);
	return 0;
}
