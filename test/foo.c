#include <stdio.h>
#include <stdlib.h>

void
hello (int a, long b)
{
	printf ("Hello World: %d - %ld!", a, b);
}

int
main (void)
{
	hello (5, 8L);
	return 0;
}
