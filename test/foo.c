#include <stdio.h>
#include <stdlib.h>

void
hello (const char *message, int a, long b)
{
	printf ("Hello World: %s - %d - %ld!", message, a, b);
}

int
main (void)
{
	hello ("Boston", 5, 8L);
	return 0;
}
