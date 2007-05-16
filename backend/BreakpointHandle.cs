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
		ILoadHandler load_handler;
		TargetFunctionType function;
		SourceMethod source;
		int line = -1;
		int index = -1;
		int domain;

		public FunctionBreakpointHandle (Breakpoint bpt, int domain, SourceMethod source)
			: base (bpt)
		{
			this.domain = domain;
			this.source = source;
		}

		public FunctionBreakpointHandle (Breakpoint bpt, int domain,
						 SourceMethod source, int line)
			: this (bpt, domain, source)
		{
			this.line = line;
		}

		public FunctionBreakpointHandle (Breakpoint bpt, int domain,
						 TargetFunctionType function)
			: this (bpt, domain, function.Source, -1)
		{
			this.function = function;
		}

		public override void Insert (Thread target)
		{
			if ((load_handler != null) || (index > 0))
				return;

			if ((function != null) && function.IsLoaded) {
				index = target.InsertBreakpoint (Breakpoint, function);
				return;
			}

			load_handler = source.SourceFile.Module.SymbolFile.RegisterLoadHandler (
				target, source, method_loaded, null);
		}

		public override void Remove (Thread target)
		{
			if (index > 0)
				target.RemoveBreakpoint (index);

			if (load_handler != null)
				load_handler.Remove ();

			load_handler = null;
			index = -1;
		}

		protected TargetAddress GetAddress (int domain)
		{
			Method method = source.GetMethod (domain);
			if (method == null)
				return TargetAddress.Null;

			if (line != -1) {
				if (method.HasSource)
					return method.Source.Lookup (line);
				else
					return TargetAddress.Null;
			} else if (method.HasMethodBounds)
				return method.MethodStartAddress;
			else
				return method.StartAddress;
		}

		// <summary>
		//   The method has just been loaded, lookup the breakpoint
		//   address and actually insert it.
		// </summary>
		public void method_loaded (TargetMemoryAccess target,
					   SourceMethod source, object data)
		{
			load_handler = null;

			TargetAddress address = GetAddress (domain);
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
	}
}
