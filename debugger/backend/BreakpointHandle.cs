using System;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Backends
{
	internal abstract class BreakpointHandle : DebuggerMarshalByRefObject
	{
		public readonly Breakpoint Breakpoint;

		protected BreakpointHandle (Breakpoint breakpoint)
		{
			this.Breakpoint = breakpoint;
		}

		internal abstract bool Insert (SingleSteppingEngine sse);

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

		internal override bool Insert (SingleSteppingEngine sse)
		{
			throw new InternalError ();
		}

		public override void Insert (Thread target)
		{
			throw new InternalError ();
		}

		public override void Remove (Thread target)
		{
			if (index > 0)
				target.RemoveBreakpoint (index);
			index = -1;
		}
	}

	internal class AddressBreakpointHandle : BreakpointHandle
	{
		public readonly TargetAddress Address;
		int index = -1;

		public AddressBreakpointHandle (Breakpoint breakpoint, TargetAddress address)
			: base (breakpoint)
		{
			this.Address = address;
		}

		internal override bool Insert (SingleSteppingEngine sse)
		{
			index = sse.Inferior.InsertBreakpoint (Breakpoint, Address);
			return false;
		}

		public override void Insert (Thread target)
		{
			index = target.InsertBreakpoint (Breakpoint, Address);
		}

		public override void Remove (Thread target)
		{
			if (index > 0)
				target.RemoveBreakpoint (index);
			index = -1;
		}
	}

	internal class FunctionBreakpointHandle : BreakpointHandle
	{
		TargetFunctionType function;
		bool has_load_handler;
		int line = -1;
		int index = -1;
		int domain;

		public FunctionBreakpointHandle (Breakpoint bpt, int domain,
						 TargetFunctionType function, int line)
			: base (bpt)
		{
			this.function = function;
			this.domain = domain;
			this.line = line;
		}

		internal TargetFunctionType Function {
			get { return function; }
		}

		internal override bool Insert (SingleSteppingEngine sse)
		{
			if (has_load_handler || (index > 0))
				return false;

			// sse.PushOperation (new OperationInsertMethodBreakpoint (function, method_loaded));
			return true;
		}

		public override void Insert (Thread target)
		{
			if (has_load_handler || (index > 0))
				return;

			has_load_handler = function.InsertBreakpoint (target, MethodLoaded);
		}

		internal void MethodLoaded (TargetMemoryAccess target, Method method)
		{
			Console.WriteLine ("BREAKPOINT HANDLE LOADED: {0}", method);

			TargetAddress address;
			if (line != -1) {
				if (method.HasLineNumbers)
					address = method.LineNumberTable.Lookup (line);
				else
					address = TargetAddress.Null;
			} else if (method.HasMethodBounds)
				address = method.MethodStartAddress;
			else
				address = method.StartAddress;

			if (address.IsNull)
				return;

			try {
				index = target.InsertBreakpoint (Breakpoint, address);
			} catch (TargetException ex) {
				Report.Error ("Can't insert breakpoint {0} at {1}: {2}",
					      Breakpoint.Index, address, ex.Message);
				index = -1;
			}
		}

		public override void Remove (Thread target)
		{
			if (index > 0)
				target.RemoveBreakpoint (index);

			if (has_load_handler)
				function.RemoveBreakpoint (target);

			has_load_handler = false;
			index = -1;
		}
	}
}
