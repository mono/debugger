using System;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	public class BreakpointHandle : EventHandle
	{
		SourceLocation location;
		TargetFunctionType function;
		TargetAddress address = TargetAddress.Null;
		int breakpoint_id = -1;
		IDisposable load_handler;

		internal BreakpointHandle (TargetAccess target, Breakpoint breakpoint,
					   SourceLocation location)
			: base (breakpoint)
		{
			this.location = location;

			if (location.Method.IsLoaded)
				address = location.GetAddress ();
			EnableBreakpoint (target);
		}

		internal BreakpointHandle (TargetAccess target, Breakpoint breakpoint,
					   TargetFunctionType func)
			: base (breakpoint)
		{
			this.function = func;

			if (function.IsLoaded)
				address = function.GetMethodAddress (target);

			EnableBreakpoint (target);
		}

		internal BreakpointHandle (Breakpoint breakpoint, TargetAddress address)
			: base (breakpoint)
		{
			this.address = address;
		}

		public override bool IsEnabled {
			get { return (breakpoint_id > 0) || (load_handler != null); }
		}

		public override void Enable (TargetAccess target)
		{
			lock (this) {
				EnableBreakpoint (target);
			}
		}

		public override void Disable (TargetAccess target)
		{
			lock (this) {
				DisableBreakpoint (target);
			}
		}

		public override void Remove (TargetAccess target)
		{
			if (load_handler != null) {
				load_handler.Dispose ();
				load_handler = null;
			}
			Disable (target);
		}

		void EnableBreakpoint (TargetAccess target)
		{
			lock (this) {
				if ((load_handler != null) || (breakpoint_id > 0))
					return;

				if (!address.IsNull)
					breakpoint_id = target.InsertBreakpoint (breakpoint, address);
				else if (function != null) {
					load_handler = function.DeclaringType.Module.RegisterLoadHandler (
						target, function.Source,
						new MethodLoadedHandler (method_loaded), null);
				} else if (location.Method.IsDynamic) {
					// A dynamic method is a method which may emit a
					// callback when it's loaded.  We register this
					// callback here and do the actual insertion when
					// the method is loaded.
					load_handler = location.Module.RegisterLoadHandler (
						target, location.Method,
						new MethodLoadedHandler (method_loaded),
						null);
				}
			}
		}

		void DisableBreakpoint (TargetAccess target)
		{
			lock (this) {
				if (breakpoint_id > 0)
					target.RemoveBreakpoint (breakpoint_id);

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
