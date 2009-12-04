using System;
using System.Collections.Generic;

using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;
using Mono.Debugger.Backend.Mono;

namespace Mono.Debugger.Backend
{
	internal class PendingBreakpointQueue : Queue<KeyValuePair<FunctionBreakpointHandle,BreakpointHandle.Action>>
	{
		public void Add (FunctionBreakpointHandle handle, BreakpointHandle.Action action)
		{
			Enqueue (new KeyValuePair<FunctionBreakpointHandle, BreakpointHandle.Action> (handle, action));
		}
	}

	internal abstract class BreakpointHandle : DebuggerMarshalByRefObject
	{
		internal enum Action {
			Insert,
			Remove
		}

		public readonly Breakpoint Breakpoint;

		protected BreakpointHandle (Breakpoint breakpoint)
		{
			this.Breakpoint = breakpoint;
		}

		public abstract void Insert (Inferior target);

		public abstract void Remove (Inferior target);

		public abstract void Insert (Thread target);

		public abstract void Remove (Thread target);
	}

	internal class SimpleBreakpointHandle : BreakpointHandle
	{
		int index;

		internal SimpleBreakpointHandle (Breakpoint breakpoint, int index)
			: base (breakpoint)
		{
			this.index = index;
		}

		public override void Insert (Inferior target)
		{
			throw new InternalError ();
		}

		public override void Insert (Thread target)
		{
			throw new InternalError ();
		}

		public override void Remove (Inferior target)
		{
			if (index > 0)
				target.RemoveBreakpoint (this);
			index = -1;
		}

		public override void Remove (Thread target)
		{
			if (index > 0)
				target.RemoveBreakpoint (this);
			index = -1;
		}
	}

	internal class AddressBreakpointHandle : BreakpointHandle
	{
		public readonly TargetAddress Address;
		bool has_breakpoint;

		public AddressBreakpointHandle (Breakpoint breakpoint, TargetAddress address)
			: base (breakpoint)
		{
			this.Address = address;
		}

		public override void Insert (Thread target)
		{
			target.InsertBreakpoint (this, Address, -1);
			has_breakpoint = true;
		}

		public override void Remove (Thread target)
		{
			if (has_breakpoint)
				target.RemoveBreakpoint (this);
			has_breakpoint = false;
		}

		public override void Insert (Inferior inferior)
		{
			inferior.InsertBreakpoint (this, Address, -1);
			has_breakpoint = true;
		}

		public override void Remove (Inferior inferior)
		{
			if (has_breakpoint)
				inferior.RemoveBreakpoint (this);
			has_breakpoint = false;
		}
	}

	internal abstract class FunctionBreakpointHandle : BreakpointHandle
	{
		TargetFunctionType function;
		int line = -1, column;

		internal int Index {
			get; private set;
		}

		protected FunctionBreakpointHandle (Breakpoint bpt, TargetFunctionType function,
						    int line, int column)
			: base (bpt)
		{
			this.function = function;
			this.line = line;
			this.column = column;

			this.Index = MonoLanguageBackend.GetUniqueID ();
		}

		internal TargetFunctionType Function {
			get { return function; }
		}

		public int Line {
			get { return line; }
		}

		public int Column {
			get { return column; }
		}

		public override void Insert (Inferior target)
		{
			throw new InternalError ();
		}

		public override void Remove (Inferior target)
		{
			throw new InternalError ();
		}

		internal abstract void MethodLoaded (TargetAccess target, Method method);

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2})", GetType (), function, line);
		}
	}
}
