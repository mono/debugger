#include <stdio.h>
#include <stdlib.h>

static int
foo (int a, int b, float f)
{
	return a + 5 * b;
}

static int
test (void)
{
	register int a = 5;
	register int b = 29;
	register float f = 3.14F;
	register d;

	d = foo (a, b, f);

	printf ("Test: %d - %d - %f - %d\n", a, b, f, d);

	return foo (a, d, f);
}

int
main (void)
{
	return test ();
}
