using System;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestMultiThread2 : TestSuite
	{
		public TestMultiThread2 ()
			: base ("TestMultiThread2")
		{ }

		[Test]
		[Category("Threads")]
		public void Main ()
		{
			Process process = Start ();
			Assert.IsTrue (process.IsManaged);
			Assert.IsTrue (process.MainThread.IsStopped);

			Thread thread = process.MainThread;
			AssertStopped (thread, "main", "X.Main()");

			AssertExecute ("next");
			Thread child = AssertThreadCreated ();
			AssertHitBreakpoint (child, "thread main", "X.ThreadMain()");

			AssertExecute ("kill");
		}
	}
}
