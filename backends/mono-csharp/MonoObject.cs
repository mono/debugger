using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoObject : ITargetObject
	{
		StackFrame frame;
		MonoVariable variable;
		bool is_local;
		ITargetLocation location;

		public MonoObject (StackFrame frame, MonoVariable variable, bool is_local)
		{
			this.frame = frame;
			this.variable = variable;
			this.is_local = is_local;

			if (variable.VariableInfo.Mode == VariableInfo.AddressMode.Stack)
				location = new TargetStackLocation (
					variable.Backend, frame, is_local, variable.VariableInfo.Offset,
					variable.StartScope, variable.EndScope);
		}

		public IVariable Variable {
			get {
				return variable;
			}
		}

		public ITargetLocation Location {
			get {
				if (location == null)
					throw new LocationInvalidException ();

				return location;
			}
		}
	}
}
