#include <stdio.h>
#include <stdlib.h>

void
crashing_here (int *ptr)
{
	*ptr = 4;
}

void
hello_world (const char *message, int a)
{
	printf (message);
	// crashing_here (NULL);
}

int
main (void)
{
	int a = 5;
	hello_world ("Hello World", a);
	return 0;
}
