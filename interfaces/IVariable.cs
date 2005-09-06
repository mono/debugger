namespace Mono.Debugger.Languages
{
	// <summary>
	//   This interface provides information about a variable in the target application.
	// </summary>
	public interface IVariable
	{
		string Name {
			get;
		}

		ITargetType Type {
			get;
		}

		// <summary>
		//   Checks whether the variable is alive at @address, but without actually
		//   trying to access the variable.  The implementation just checks the data
		//   from the symbol file and - if appropriate - from the JIT to find out
		//   whether the specified address is within the variable's live range.
		// </summary>
		bool IsAlive (TargetAddress address);

		// <summary>
		//   Checks whether the variable can actually be accessed at the specified
		//   address.  Note that this call returns false if the variable is a null
		//   pointer.
		// </summary>
		bool CheckValid (StackFrame frame);

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
		ITargetObject GetObject (StackFrame frame);

		bool CanWrite {
			get;
		}

		void SetObject (StackFrame frame, ITargetObject obj);
	}
}
