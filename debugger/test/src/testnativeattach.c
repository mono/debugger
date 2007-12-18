#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>

int
main (void)
{
	for (;;) {
		printf ("Hello World!\n");
		fflush (stdout);
		sleep (1);
	}

	return 0;
}
