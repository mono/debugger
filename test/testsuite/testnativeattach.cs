using System;
using SD = System.Diagnostics;
using ST = System.Threading;
using NUnit.Framework;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Frontend;

namespace Mono.Debugger.Tests
{
	[TestFixture]
	public class testnativeattach : TestSuite
	{
		SD.Process child;

		public testnativeattach ()
			: base ("testnativeattach", "testnativeattach.c")
		{ }

		public override void SetUp ()
		{
			base.SetUp ();

			SD.ProcessStartInfo start = new SD.ProcessStartInfo (ExeFileName);

			start.UseShellExecute = false;
			start.RedirectStandardOutput = true;
			start.RedirectStandardError = true;

			child = SD.Process.Start (start);
			child.StandardOutput.ReadLine ();
		}

		public override void TearDown ()
		{
			base.TearDown ();

			if (!child.HasExited)
				child.Kill ();
		}

		[Test]
		[Category("Attach")]
		public void Main ()
		{
			Process process = Interpreter.Attach (child.Id);
			Assert.IsTrue (process.MainThread.IsStopped);

			AssertStopped (process.MainThread, null, -1);

			StackFrame frame = process.MainThread.CurrentFrame;
			Assert.IsNotNull (frame);
			Backtrace bt = process.MainThread.GetBacktrace (-1);
			if (bt.Count < 1)
				Assert.Fail ("Cannot get backtrace.");

			process.Detach ();
			AssertProcessExited (process);
			AssertTargetExited ();
		}

		[Test]
		[Category("Attach")]
		public void AttachAgain ()
		{
			Process process = Interpreter.Attach (child.Id);
			Assert.IsTrue (process.MainThread.IsStopped);

			AssertStopped (process.MainThread, null, -1);

			StackFrame frame = process.MainThread.CurrentFrame;
			Assert.IsNotNull (frame);
			Backtrace bt = process.MainThread.GetBacktrace (-1);
			if (bt.Count < 1)
				Assert.Fail ("Cannot get backtrace.");

			process.Detach ();
			AssertProcessExited (process);
			AssertTargetExited ();
		}

		[Test]
		[Category("Attach")]
		public void Kill ()
		{
			Process process = Interpreter.Attach (child.Id);
			Assert.IsTrue (process.MainThread.IsStopped);

			AssertStopped (process.MainThread, null, -1);

			StackFrame frame = process.MainThread.CurrentFrame;
			Assert.IsNotNull (frame);
			Backtrace bt = process.MainThread.GetBacktrace (-1);
			if (bt.Count < 1)
				Assert.Fail ("Cannot get backtrace.");

			Interpreter.Kill ();
			AssertTargetExited ();

			child.WaitForExit ();
		}
	}
}
