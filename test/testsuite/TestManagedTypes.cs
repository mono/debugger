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

		[Test]
		[Category("ManagedTypes")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;

			AssertStopped (thread, "main", "X.Main()");

			AssertExecute ("continue");
			AssertHitBreakpoint (thread, "simple", "X.Simple()");

			AssertType (thread, "a", "int");
			AssertPrint (thread, "a", "(int) 5");
			AssertType (thread, "b", "long");
			AssertPrint (thread, "b", "(long) 7");
			AssertType (thread, "f", "float");
			AssertPrint (thread, "f", "(float) 0.7142857");
			AssertType (thread, "hello", "string");
			AssertPrint (thread, "hello", "(string) \"Hello World\"");

			AssertExecute ("set a = 9");
			AssertExecute ("set hello = \"Monkey\"");

			AssertPrint (thread, "a", "(int) 9");
			AssertPrint (thread, "hello", "(string) \"Monkey\"");

			AssertExecute ("continue");
			AssertTargetOutput ("9");
			AssertTargetOutput ("7");
			AssertTargetOutput ("0.7142857");
			AssertTargetOutput ("Monkey");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, "boxed valuetype", "X.BoxedValueType()");

			AssertPrint (thread, "a", "(int) 5");
			AssertPrint (thread, "boxed_a", "(object) &(int) 5");
			AssertPrint (thread, "*boxed_a", "(int) 5");

			AssertExecute ("continue");
			AssertTargetOutput ("5");
			AssertTargetOutput ("5");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, "boxed reftype", "X.BoxedReferenceType()");

			AssertPrint (thread, "hello", "(string) \"Hello World\"");
			AssertPrint (thread, "boxed_hello", "(object) &(string) \"Hello World\"");
			AssertPrint (thread, "*boxed_hello", "(string) \"Hello World\"");

			AssertExecute ("continue");
			AssertTargetOutput ("Hello World");
			AssertTargetOutput ("Hello World");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, "simple array", "X.SimpleArray()");

			AssertPrint (thread, "a", "(int[]) [ 3, 4, 5 ]");
			AssertPrint (thread, "a[1]", "(int) 4");
			AssertExecute ("set a[2] = 9");
			AssertPrint (thread, "a[2]", "(int) 9");
			AssertPrint (thread, "a.Length", "(int) 3");
			AssertPrint (thread, "a.GetRank ()", "(int) 1");

			AssertExecute ("continue");
			AssertTargetOutput ("9");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, "multi valuetype array",
					     "X.MultiValueTypeArray()");

			AssertPrint (thread, "a", "(int[,]) [ [ 6, 7, 8 ], [ 9, 10, 11 ] ]");
			AssertPrintException (thread, "a[1]",
					      "Index of array expression `a' out of bounds.");
			AssertPrint (thread, "a[1,2]", "(int) 11");
			AssertPrintException (thread, "a[2]",
					      "Index of array expression `a' out of bounds.");
			AssertExecute ("set a[1,2] = 50");
			AssertPrint (thread, "a.Length", "(int) 6");
			AssertPrint (thread, "a.GetRank ()", "(int) 2");

			AssertExecute ("continue");
			AssertTargetOutput ("50");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, "string array", "X.StringArray()");

			AssertPrint (thread, "a", "(string[]) [ \"Hello\", \"World\" ]");
			AssertPrint (thread, "a[1]", "(string) \"World\"");
			AssertExecute ("set a[1] = \"Trier\"");
			AssertPrint (thread, "a", "(string[]) [ \"Hello\", \"Trier\" ]");
			AssertPrint (thread, "a[1]", "(string) \"Trier\"");

			AssertExecute ("continue");
			AssertTargetOutput ("System.String[]");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, "multi string array", "X.MultiStringArray()");

			AssertPrint (thread, "a",
				     "(string[,]) [ [ \"Hello\", \"World\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Ximian\", \"Monkeys\" ] ]");
			AssertPrint (thread, "a[2,1]", "(string) \"Monkeys\"");
			AssertExecute ("set a[2,1] = \"Primates\"");
			AssertPrint (thread, "a",
				     "(string[,]) [ [ \"Hello\", \"World\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Ximian\", \"Primates\" ] ]");
			AssertPrint (thread, "a[2,1]", "(string) \"Primates\"");
			AssertExecute ("set a[0,1] = \"Lions\"");
			AssertPrint (thread, "a",
				     "(string[,]) [ [ \"Hello\", \"Lions\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Ximian\", \"Primates\" ] ]");
			AssertPrint (thread, "a[0,1]", "(string) \"Lions\"");
			AssertPrint (thread, "a[2,1]", "(string) \"Primates\"");

			AssertExecute ("set a[0,0] = \"Birds\"");
			AssertExecute ("set a[2,0] = \"Dogs\"");
			AssertPrint (thread, "a",
				     "(string[,]) [ [ \"Birds\", \"Lions\" ], " +
				     "[ \"New York\", \"Boston\" ], [ \"Dogs\", \"Primates\" ] ]");

			AssertExecute ("continue");
			AssertTargetOutput ("System.String[,]");
			AssertTargetOutput ("51.2");
			AssertTargetOutput ("Hello World");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, "struct type", "X.StructType()");

			AssertPrint (thread, "a",
				     "(A) { a = 5, b = 256, c = \"New England Patriots\", f = 51.2 }");

			AssertExecute ("continue");
			AssertTargetOutput ("A");
			AssertTargetOutput ("5");
			AssertTargetOutput ("3.14");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, "class type", "X.ClassType()");

			AssertPrint (thread, "b",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");

			AssertExecute ("continue");
			AssertTargetOutput ("B");
			AssertTargetOutput ("8");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, "inherited class type",
					     "X.InheritedClassType()");

			AssertPrint (thread, "c",
				     "(C) { <B> = { a = 5, b = 256, c = \"New England Patriots\" }, " +
				     "a = 8, f = 3.14 }");

			AssertPrint (thread, "b",
				     "(C) { <B> = { a = 5, b = 256, c = \"New England Patriots\" }, " +
				     "a = 8, f = 3.14 }");
			AssertPrint (thread, "(B) c",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");

			AssertExecute ("continue");
			AssertTargetOutput ("5");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, "complex struct type",
					     "X.ComplexStructType()");

			AssertPrint (thread, "d.a",
				     "(A) { a = 5, b = 256, c = \"New England Patriots\", f = 51.2 }");
			AssertPrint (thread, "d.b",
				     "(B) { a = 5, b = 256, c = \"New England Patriots\" }");
			AssertPrint (thread, "d.c",
				     "(C) { <B> = { a = 5, b = 256, c = \"New England Patriots\" }, " +
				     "a = 8, f = 3.14 }");
			AssertPrint (thread, "d.s",
				     "(string[]) [ \"Eintracht Trier\" ]");

			AssertExecute ("continue");
			AssertTargetOutput ("Eintracht Trier");
			AssertNoTargetOutput ();

			AssertHitBreakpoint (thread, "function struct type",
					     "X.FunctionStructType()");

			AssertPrint (thread, "e", "(E) { a = 9 }");
			AssertPrint (thread, "e.a", "(int) 9");
			AssertPrint (thread, "e.Foo (5)", "(long) 5");

			AssertExecute ("continue");
			AssertTargetOutput ("9");

			AssertHitBreakpoint (thread, "simple types", "X.SimpleTypes()");
			AssertPrint (thread, "a", "(byte) 1");
			AssertPrint (thread, "b", "(sbyte) -2");
			AssertPrint (thread, "c", "(short) -3");
			AssertPrint (thread, "d", "(ushort) 4");
			AssertPrint (thread, "e", "(uint) 5");
			AssertPrint (thread, "f", "(int) -6");
			AssertPrint (thread, "g", "(long) -7");
			AssertPrint (thread, "h", "(ulong) 8");
			AssertPrint (thread, "i", "(float) 9.1");
			AssertPrint (thread, "j", "(double) 2.3");
			AssertPrint (thread, "k", "(System.Decimal) { \"123456789\" }");

			AssertType (thread, "3", "int");
			AssertType (thread, "-3", "int");
			AssertPrint (thread, "2147483647", "2147483647"); // Int32.MaxValue
			AssertType (thread, "2147483647", "int");
			AssertPrint (thread, "-2147483648", "-2147483648"); // Int32.MinValue
			AssertType (thread, "-2147483648", "int");
			AssertPrint (thread, "4294967295u", "4294967295"); // UInt32.MaxValue
			AssertType (thread, "4294967295u", "uint");

			AssertPrint (thread, "-9223372036854775808l", "-9223372036854775808");
			AssertPrint (thread, "18446744073709551615lu", "18446744073709551615");

			AssertExecute ("set e = 4294967295u");
			AssertExecute ("set f = -2147483648");
			AssertExecute ("set g = -9223372036854775808l");
			AssertExecute ("set h = 18446744073709551615lu");

			AssertPrint (thread, "2.7182818284590452354", "2.71828182845905");
			AssertPrint (thread, "2.7182818284590452354f", "2.718282");
			AssertPrint (thread, "2.7182818284590452354d", "2.71828182845905");
			AssertPrint (thread, "3.40282346638528859e38f", "3.402823E+38");
			AssertPrint (thread, "-3.40282346638528859e38f", "-3.402823E+38");
			AssertType (thread, "2.7182818284590452354", "double");
			AssertType (thread, "2.7182818284590452354f", "float");
			AssertType (thread, "2.7182818284590452354d", "double");
			AssertType (thread, "3.40282346638528859e38f", "float");
			AssertType (thread, "-3.40282346638528859e38f", "float");

			AssertExecute ("set i = -3.40282346638528859e38f");
			AssertExecute ("set j = 2.7182818284590452354d");

			AssertPrint (thread, "i", "(float) -3.402823E+38");
			AssertPrint (thread, "j", "(double) 2.71828182845905");

			AssertExecute ("set c = (short) -32768");
			AssertExecute ("set d = (ushort) 65535");

			AssertExecute ("set a = (byte) 255");
			AssertExecute ("set b = (sbyte) -128");

			AssertPrint (thread, "a", "(byte) 255");
			AssertPrint (thread, "b", "(sbyte) -128");
			AssertPrint (thread, "c", "(short) -32768");
			AssertPrint (thread, "d", "(ushort) 65535");

			AssertPrint (thread, "-79228162514264337593543950335m",
				     "-79228162514264337593543950335");
			AssertPrint (thread, "79228162514264337593543950335m",
				     "79228162514264337593543950335");

			// AssertType (thread, "-79228162514264337593543950335m", "decimal");
			// AssertType (thread, "79228162514264337593543950335m", "decimal");
			// AssertExecute ("set k = -79228162514264337593543950335m");

			AssertExecute ("continue");
			AssertTargetOutput ("255 -128 -32768 65535 4294967295 -2147483648 -2147483648 " +
					    "-9223372036854775808 18446744073709551615 " +
					    "-3.402823E+38 2.71828182845905 123456789");

			AssertTargetExited (thread.Process);
		}
	}
}
