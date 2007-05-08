using System;
using System.Text;
using System.Collections;
using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	[Serializable]
	public enum DisplayFormat
	{
		Default,
		HexaDecimal,
		Address,
		Object
	}

	/// <summary>
	///   This interface controls how things are being displayed to the
	///   user, for instance the current stack frame or variables from
	///   the target.
	/// </summary>
	[Serializable]
	public abstract class Style
	{
		public abstract string Name {
			get;
		}

		public abstract string ShowVariableType (TargetType type, string name);

		public abstract string PrintVariable (TargetVariable variable, StackFrame frame);

		public abstract string FormatObject (Thread target, object obj,
						     DisplayFormat format);

		public abstract string FormatType (Thread target, TargetType type);
	}
}
