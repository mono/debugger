using System;
using System.IO;
using System.Text;

namespace Mono.Debugger.Backends
{
	internal enum StepMode
	{
		// <summary>
		//   Step a single machine instruction.
		// </summary>
		SingleInstruction,

		// <summary>
		//   Step a single machine instruction, but step over function calls.
		// </summary>
		NextInstruction,

		// <summary>
		//   Single-step until leaving the specified step frame or entering a method.
		// </summary>
		StepFrame,

		// <summary>
		//   Single-step until leaving the specified step frame and never enter any
		//   methods.
		// </summary>
		Finish
	}

	internal sealed class StepFrame
	{
		TargetAddress start, end;
		ILanguageBackend language;
		SimpleStackFrame stack;
		StepMode mode;

		internal StepFrame (ILanguageBackend language, StepMode mode)
			: this (TargetAddress.Null, TargetAddress.Null, null, language, mode)
		{ }

		internal StepFrame (TargetAddress start, TargetAddress end, SimpleStackFrame stack,
				    ILanguageBackend language, StepMode mode)
		{
			this.start = start;
			this.end = end;
			this.stack = stack;
			this.language = language;
			this.mode = mode;
		}

		public StepMode Mode {
			get {
				return mode;
			}
		}

		public TargetAddress Start {
			get {
				return start;
			}
		}

		public TargetAddress End {
			get {
				return end;
			}
		}

		public SimpleStackFrame StackFrame {
			get {
				return stack;
			}
		}

		internal ILanguageBackend Language {
			get {
				return language;
			}
		}

		public override string ToString ()
		{
			return String.Format ("StepFrame ({0:x},{1:x},{2},{3})",
					      Start, End, Mode, Language);
		}
	}
}
