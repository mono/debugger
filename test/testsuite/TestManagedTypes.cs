using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestManagedTypes : TestSuite
	{
		public TestManagedTypes ()
			: base ("TestManagedTypes")
		{ }

		const int LineSimple = 120;
		const int LineBoxedValueType = 132;
		const int LineBoxedReferenceType = 142;
		const int LineSimpleArray = 150;
		const int LineMultiValueArray = 157;
		const int LineStringArray = 164;
		const int LineMultiStringArray = 172;
		const int LineStructType = 179;
		const int LineClassType = 186;
		const int LineInheritedClassType = 195;
		const int LineComplexStructType = 205;
		const int LineFunctionStructType = 213;

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "X.Main()", 218);

			int bpt_simple = AssertBreakpoint (LineSimple);
			int bpt_boxed_value = AssertBreakpoint (LineBoxedValueType);
			int bpt_boxed_ref = AssertBreakpoint (LineBoxedReferenceType);
			int bpt_simple_array = AssertBreakpoint (LineSimpleArray);
			int bpt_multi_value_array = AssertBreakpoint (LineMultiValueArray);
			int bpt_string_array = AssertBreakpoint (LineStringArray);
			int bpt_multi_string_array = AssertBreakpoint (LineMultiStringArray);
			int bpt_struct_type = AssertBreakpoint (LineStructType);
			int bpt_class_type = AssertBreakpoint (LineClassType);
			int bpt_inherited_class_type = AssertBreakpoint (LineInheritedClassType);
			int bpt_complex_struct_type = AssertBreakpoint (LineComplexStructType);
			int bpt_function_struct_type = AssertBreakpoint (LineFunctionStructType);

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, bpt_simple, "X.Simple()", LineSimple);

			AssertType (thread, "a", "System.Int32");
			AssertPrint (thread, "a", "(System.Int32) 5");
			AssertType (thread, "b", "System.Int64");
			AssertPrint (thread, "b", "(System.Int64) 7");
			AssertType (thread, "f", "System.Single");
			AssertPrint (thread, "f", "(System.Single) 0.7142857");
			AssertType (thread, "hello", "System.String");
			AssertPrint (thread, "hello", "(System.String) \"Hello World\"");

			AssertExecute ("set a = 9");
			AssertExecute ("set hello = \"Monkey\"");

			AssertPrint (thread, "a", "(System.Int32) 9");
			AssertPrint (thread, "hello", "(System.String) \"Monkey\"");

			AssertExecute ("continue");
			AssertTargetOutput ("9");
			AssertTargetOutput ("7");
			AssertTargetOutput ("0.7142857");
			AssertTargetOutput ("Monkey");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_boxed_value, "X.BoxedValueType()",
					     LineBoxedValueType);

			AssertPrint (thread, "a", "(System.Int32) 5");
			AssertPrint (thread, "boxed_a", "(object) &(System.Int32) 5");
			AssertPrint (thread, "*boxed_a", "(System.Int32) 5");

			AssertExecute ("continue");
			AssertTargetOutput ("5");
			AssertTargetOutput ("5");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_boxed_ref, "X.BoxedReferenceType()",
					     LineBoxedReferenceType);

			AssertPrint (thread, "hello", "(System.String) \"Hello World\"");
			AssertPrint (thread, "boxed_hello", "(object) &(System.String) \"Hello World\"");
			AssertPrint (thread, "*boxed_hello", "(System.String) \"Hello World\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertTargetOutput ("Hello World");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_simple_array, "X.SimpleArray()",
					     LineSimpleArray);

			AssertPrint (thread, "a", "(System.Int32[]) [ 3, 4, 5 ]");
			AssertPrint (thread, "a[1]", "(System.Int32) 4");
			AssertExecute ("set a[2] = 9");
			AssertPrint (thread, "a[2]", "(System.Int32) 9");
			AssertPrint (thread, "a.Length", "(System.Int32) 3");
			AssertPrint (thread, "a.GetRank ()", "(System.Int32) 1");

			AssertExecute ("continue");
			AssertTargetOutput ("9");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_multi_value_array,
					     "X.MultiValueTypeArray()", LineMultiValueArray);

			AssertPrint (thread, "a", "(System.Int32[,]) [ [ 6, 7, 8 ], [ 9, 10, 11 ] ]");
			AssertPrintException (thread, "a[1]",
					      "Index of array expression `a' out of bounds.");
			AssertPrint (thread, "a[1,2]", "(System.Int32) 11");
			AssertPrintException (thread, "a[2]",
					      "Index of array expression `a' out of bounds.");
			AssertExecute ("set a[1,2] = 50");
			AssertPrint (thread, "a.Length", "(System.Int32) 6");
			AssertPrint (thread, "a.GetRank ()", "(System.Int32) 2");

			AssertExecute ("continue");
			AssertTargetOutput ("50");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_string_array, "X.StringArray()",
					     LineStringArray);

			AssertPrint (thread, "a", "(System.String[]) [ \"Hello\", \"World\" ]");
			AssertPrint (thread, "a[1]", "(System.String) \"World\"");
			AssertExecute ("set a[1] = \"Trier\"");
			AssertPrint (thread, "a", "(System.String[]) [ \"Hello\", \"Trier\" ]");
			AssertPrint (thread, "a[1]", "(System.String) \"Trier\"");

			AssertExecute ("continue");
			AssertTargetOutput ("System.String[]");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_multi_string_array, "X.MultiStringArray()",
					     LineMultiStringArray);

			AssertPrint (thread, "a",
				     "(System.String[,]) [ [ \"Hello\", \"World\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Ximian\", \"Monkeys\" ] ]");
			AssertPrint (thread, "a[2,1]", "(System.String) \"Monkeys\"");
			AssertExecute ("set a[2,1] = \"Primates\"");
			AssertPrint (thread, "a",
				     "(System.String[,]) [ [ \"Hello\", \"World\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Ximian\", \"Primates\" ] ]");
			AssertPrint (thread, "a[2,1]", "(System.String) \"Primates\"");
			AssertExecute ("set a[0,1] = \"Lions\"");
			AssertPrint (thread, "a",
				     "(System.String[,]) [ [ \"Hello\", \"Lions\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Ximian\", \"Primates\" ] ]");
			AssertPrint (thread, "a[0,1]", "(System.String) \"Lions\"");
			AssertPrint (thread, "a[2,1]", "(System.String) \"Primates\"");

			AssertExecute ("set a[0,0] = \"Birds\"");
			AssertExecute ("set a[2,0] = \"Dogs\"");
			AssertPrint (thread, "a",
				     "(System.String[,]) [ [ \"Birds\", \"Lions\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Dogs\", \"Primates\" ] ]");

			AssertExecute ("continue");
			AssertTargetOutput ("System.String[,]");
			AssertTargetOutput ("51.2");
			AssertTargetOutput ("Hello World");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_struct_type, "X.StructType()",
					     LineStructType);

			AssertPrint (thread, "a",
				     "(A) { a = 5, b = 256, c = \"New England Patriots\", f = 51.2 }");

			AssertExecute ("continue");
			AssertTargetOutput ("A");
			AssertTargetOutput ("5");
			AssertTargetOutput ("3.14");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_class_type, "X.ClassType()",
					     LineClassType);

			AssertPrint (thread, "b",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");

			AssertExecute ("continue");
			AssertTargetOutput ("B");
			AssertTargetOutput ("8");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_inherited_class_type,
					     "X.InheritedClassType()", LineInheritedClassType);

			AssertPrint (thread, "c",
				     "(C) { a = 8, f = 3.14 }");
			AssertPrint (thread, "b",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");
			AssertPrint (thread, "(B) c",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");

			AssertExecute ("continue");
			AssertTargetOutput ("5");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_complex_struct_type,
					     "X.ComplexStructType()", LineComplexStructType);

			AssertPrint (thread, "d.a",
				     "(A) { a = 5, b = 256, c = \"New England Patriots\", f = 51.2 }");
			AssertPrint (thread, "d.b",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");
			AssertPrint (thread, "d.c",
				     "(C) { a = 8, f = 3.14 }");
			AssertPrint (thread, "d.s",
				     "(System.String[]) [ \"Eintracht Trier\" ]");

			AssertExecute ("continue");
			AssertTargetOutput ("Eintracht Trier");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, bpt_function_struct_type,
					     "X.FunctionStructType()", LineFunctionStructType);

			AssertPrint (thread, "e", "(E) { a = 9 }");
			AssertPrint (thread, "e.a", "(System.Int32) 9");
			AssertPrint (thread, "e.Foo (5)", "(System.Int64) 5");

			AssertExecute ("continue");
			AssertTargetOutput ("9");
			AssertTargetExited (thread.Process);
		}
	}
}
