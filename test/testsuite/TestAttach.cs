using System;
using SD = System.Diagnostics;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class TestAttach : TestSuite
	{
		public TestAttach ()
			: base ("TestAttach")
		{ }

		[Test]
		[Category("NotWorking")]
		public void Main ()
		{
			SD.Process child = SD.Process.Start (MonoExecutable, ExeFileName);
			Console.WriteLine (child.Id);

			try {
				Process process = Interpreter.Attach (child.Id);
				Console.WriteLine ("TEST: {0}", process);
			} finally {
				child.Kill ();
			}
		}
	}
}
