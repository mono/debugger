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

void
loop (int a)
{
	for (;;) {
		hello ("Hello World", a);
		sleep (10);
	}
}

int
main (void)
{
	int a = 5;
	loop (a);
	return 0;
}
