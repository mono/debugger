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

	printf ("Simple: %d - %ld - %g - %s\n", a, b, f, hello); // @MDB BREAKPOINT: simple
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

	print_test_struct (&s); // @MDB BREAKPOINT: struct
}

void
test_struct_2 (void)
{
	TestStruct s;

	s.a = 5;
	s.b = 7;
	s.f = (float) s.b / (float) s.a;
	s.hello = "Hello World";

	print_test_struct (&s); // @MDB BREAKPOINT: struct2
}

void
test_struct_3 (void)
{
	Anonymous s;

	s.a = 5;
	s.foo.b = 800;
	s.bar.b = 9000;
	printf ("Test: %d - %ld,%ld\n", s.a, s.foo.b, s.bar.b); // @MDB BREAKPOINT: struct3
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

	test.foo = test_func; // @MDB BREAKPOINT: function struct
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

	printf ("Bitfield: %x\n", bitfield.a); // @MDB BREAKPOINT: bitfield
}

void
test_list (void)
{
	List list;

	list.a = 9;
	list.next = &list;

	printf ("%d\n", list.next->a); // @MDB BREAKPOINT: list
}

typedef void (*test_func_ptr) (int);

void
test_function_ptr (void)
{
	void (*func_ptr) (int) = test_func;
	test_func_ptr func_ptr2 = func_ptr;
	test_func_ptr *func_ptr3 = &func_ptr;

	(* func_ptr) (3); // @MDB BREAKPOINT: funcptr
	(* func_ptr2) (9);
	(** func_ptr3) (11);
}

typedef struct {
	int simple_array [3];
	long multi_array [2] [3];
	float anonymous_array [];
} TestArray;

TestArray *
allocate_array (void)
{
	TestArray *test = malloc (sizeof (TestArray) + 2 * sizeof (long));
	int i, j;

	test->simple_array [0] = 8192;
	test->simple_array [1] = 55;
	test->simple_array [2] = 71;

	for (i = 0; i < 2; i++) {
		for (j = 0; j < 3; j++)
			test->multi_array [i][j] = (i + 2) << (j + 3);
	}

	test->anonymous_array [0] = 3.141593;
	test->anonymous_array [1] = 2.718282;
	return test;
}

void
test_array (void)
{
	TestArray *array = allocate_array ();
	free (array);				// @MDB BREAKPOINT: array
}

int
main (void)
{
	setbuf (stdout, NULL);			// @MDB LINE: main
	simple ();
	test_struct ();
	test_struct_2 ();
	test_struct_3 ();
	test_function_struct ();
	test_bitfield ();
	test_list ();
	test_function_ptr ();
	test_array ();
	return 0;
}
