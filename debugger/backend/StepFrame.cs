using System;
using System.IO;
using System.Text;

using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Backend
{
	internal enum StepMode
	{
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
		Finish
	}

	internal sealed class StepFrame
	{
		TargetAddress start, end;
		Language language;
		StackFrame stack;
		StepMode mode;

		internal StepFrame (Language language, StepMode mode)
			: this (TargetAddress.Null, TargetAddress.Null, null, language, mode)
		{ }

		internal StepFrame (TargetAddress start, TargetAddress end, StackFrame stack,
				    Language language, StepMode mode)
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
