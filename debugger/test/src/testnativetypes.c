#include <stdlib.h>
#include <stdio.h>

typedef struct _TestStruct TestStruct;
typedef struct _FunctionStruct FunctionStruct;

typedef struct {
	int a;
	struct {
		long b;
	} foo, bar;
} Anonymous;

struct _TestStruct
{
	int a;
	long b;
	float f;
	const char *hello;
};

struct _FunctionStruct
{
	void (* foo) (int a);
};

typedef struct
{
	int a : 1;
	int b : 5;
	int c : 3;
	int d : 5;
	int e : 9;
	int f : 8;
} BitField;

typedef struct _List List;

struct _List {
	int a;
	List *next;
};

void
simple (void)
{
	int a = 5;
	long b = 7;
	float f = (float) a / (float) b;

	const char *hello = "Hello World";

	printf ("Simple: %d - %ld - %g - %s\n", a, b, f, hello);
}

void
print_test_struct (struct _TestStruct *s)
{
	printf ("Struct: %d - %ld - %g - %s\n", s->a, s->b, s->f, s->hello);
}

void
test_struct (void)
{
	struct _TestStruct s;

	s.a = 5;
	s.b = 7;
	s.f = (float) s.b / (float) s.a;
	s.hello = "Hello World";

	print_test_struct (&s);
}

void
test_struct_2 (void)
{
	TestStruct s;

	s.a = 5;
	s.b = 7;
	s.f = (float) s.b / (float) s.a;
	s.hello = "Hello World";

	print_test_struct (&s);
}

void
test_struct_3 (void)
{
	Anonymous s;

	s.a = 5;
	s.foo.b = 800;
	s.bar.b = 9000;
	printf ("Test: %d - %ld,%ld\n", s.a, s.foo.b, s.bar.b);
}

void
test_func (int a)
{
	printf ("Test: %d\n", a);
}

void
test_function_struct (void)
{
	FunctionStruct test;

	test.foo = test_func;
}

void
test_bitfield (void)
{
	BitField bitfield;

	bitfield.a = 1;
	bitfield.b = 3;
	bitfield.c = 4;
	bitfield.d = 9;
	bitfield.e = 15;
	bitfield.f = 8;

	printf ("Bitfield: %x\n", * ((int *) &bitfield));
}

void
test_list (void)
{
	List list;

	list.a = 9;
	list.next = &list;

	printf ("%d\n", list.next->a);
}

int
main (void)
{
	setbuf (stdout, NULL);
	simple ();
	test_struct ();
	test_struct_2 ();
	test_struct_3 ();
	test_function_struct ();
	test_bitfield ();
	test_list ();
	return 0;
}
