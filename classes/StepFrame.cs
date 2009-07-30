using System;
using System.IO;
using System.Text;

using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	public enum StepMode
	{
		// <summary>
		//   Resume the target and run until an optional end location.
		// </summary>
		Run,

		// <summary>
		//   Step a single machine instruction, but step over trampolines.
		// </summary>
		SingleInstruction,

		// <summary>
		//   Step a single macihne instruction, always step into method calls.
		// </summary>
		NativeInstruction,

		// <summary>
		//   Step a single machine instruction, but step over function calls.
		// </summary>
		NextInstruction,

		// <summary>
		//   Step one source line.
		// </summary>
		SourceLine,

		// <summary>
		//   Step one source line, but step over method calls.
		// </summary>
		NextLine,

		// <summary>
		//   Single-step until leaving the specified step frame or entering a method.
		// </summary>
		StepFrame,

		// <summary>
		//   Single-step until leaving the specified step frame and never enter any
		//   methods.
		// </summary>
		Finish,

		FinishNative
	}

	[Serializable]
	public sealed class StepFrame
	{
		TargetAddress start, end;
		Language language;
		StackFrame stack;
		StepMode mode;

		public StepFrame (Language language, StepMode mode)
			: this (language, mode, null, TargetAddress.Null, TargetAddress.Null)
		{ }

		public StepFrame (Language language, StepMode mode, TargetAddress until)
			: this (language, mode, null, TargetAddress.Null, until)
		{ }

		public StepFrame (Language language, StepMode mode, StackFrame stack,
				  TargetAddress start, TargetAddress end)
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

		public TargetAddress Until {
			get {
				if ((mode != StepMode.Run) && (mode != StepMode.FinishNative))
					throw new InvalidOperationException ();

				return end;
			}
		}

		public StackFrame StackFrame {
			get {
				return stack;
			}
		}

		public Language Language {
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
