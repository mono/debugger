using System;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   This interface provides information about a variable in the target application.
	// </summary>
	public abstract class TargetVariable : DebuggerMarshalByRefObject
	{
		public abstract string Name {
			get;
		}

		public abstract TargetType Type {
			get;
		}

		// <summary>
		//   Checks whether the variable is accessible in the lexical scope around
		//   address @address, but without actually trying to access the variable.
		// </summary>
		public abstract bool IsInScope (TargetAddress address);

		// <summary>
		//   Checks whether the variable is alive at @address, but without actually
		//   trying to access the variable.  The implementation just checks the data
		//   from the symbol file and - if appropriate - from the JIT to find out
		//   whether the specified address is within the variable's live range.
		// </summary>
		public abstract bool IsAlive (TargetAddress address);

		// <summary>
		//   Retrieve an instance of this variable from the stack-frame @frame.
		//   May only be called if Type.HasObject is true.
		// </summary>
		// <remarks>
		//   An instance of IVariable contains information about a variable (for
		//   instance a parameter of local variable of a method), but it's not
		//   bound to any particular target location.  This also means that it won't
		//   get invalid after the target exited.
		// </remarks>
		public abstract TargetObject GetObject (StackFrame frame);

		public abstract string PrintLocation (StackFrame frame);

		public abstract bool CanWrite {
			get;
		}

		public abstract void SetObject (StackFrame frame, TargetObject obj);
	}
}
