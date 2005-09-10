using System;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	public class BreakpointHandle : EventHandle
	{
		SourceLocation location;
		ITargetFunctionType function;
		TargetAddress address = TargetAddress.Null;
		int breakpoint_id = -1;
		IDisposable load_handler;

		internal BreakpointHandle (Process process, Breakpoint breakpoint,
					   SourceLocation location)
			: base (breakpoint)
		{
			this.location = location;

			if (location.Method.IsLoaded)
				address = location.GetAddress ();
			EnableBreakpoint (process);
		}

		internal BreakpointHandle (Process process, Breakpoint breakpoint,
					   ITargetFunctionType func)
			: base (breakpoint)
		{
			this.function = func;

			EnableBreakpoint (process);
		}

		internal BreakpointHandle (Breakpoint breakpoint, TargetAddress address)
			: base (breakpoint)
		{
			this.address = address;
		}

		public override bool IsEnabled {
			get { return (breakpoint_id > 0) || (load_handler != null); }
		}

		public override void Enable (Process process)
		{
			lock (this) {
				EnableBreakpoint (process);
			}
		}

		public override void Disable (Process process)
		{
			lock (this) {
				DisableBreakpoint (process);
			}
		}

		public override void Remove (Process process)
		{
			if (load_handler != null) {
				load_handler.Dispose ();
				load_handler = null;
			}
			Disable (process);
		}

		void EnableBreakpoint (Process process)
		{
			lock (this) {
				if ((load_handler != null) || (breakpoint_id > 0))
					return;

				if (!address.IsNull)
					breakpoint_id = process.InsertBreakpoint (
						breakpoint, address);
				else if (function != null)
					breakpoint_id = process.InsertBreakpoint (
						breakpoint, function);
				else if (location.Method.IsDynamic) {
					// A dynamic method is a method which may emit a
					// callback when it's loaded.  We register this
					// callback here and do the actual insertion when
					// the method is loaded.
					load_handler = location.Module.RegisterLoadHandler (
						process, location.Method,
						new MethodLoadedHandler (method_loaded),
						null);
				}
			}
		}

		void DisableBreakpoint (Process process)
		{
			lock (this) {
				if (breakpoint_id > 0)
					process.RemoveBreakpoint (breakpoint_id);

				if (load_handler != null)
					load_handler.Dispose ();

				load_handler = null;
				breakpoint_id = -1;
			}
		}

		public SourceLocation SourceLocation {
			get { return location; }
		}

		// <summary>
		//   The method has just been loaded, lookup the breakpoint
		//   address and actually insert it.
		// </summary>
		void method_loaded (ITargetMemoryAccess target, SourceMethod method, object data)
		{
			load_handler = null;

			address = location.GetAddress ();
			if (address.IsNull)
				return;

			breakpoint_id = target.InsertBreakpoint (breakpoint, address);
		}

		public TargetAddress Address {
			get { return address; }
		}
	}
}
