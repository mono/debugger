using System;
using System.Runtime.Serialization;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public class BreakpointHandle
	{
		Breakpoint breakpoint;
		SourceLocation location;

		private BreakpointHandle (Process process, Breakpoint breakpoint,
					  SourceLocation location)
		{
			this.breakpoint = breakpoint;
			this.location = location;

			if (location.Method.IsLoaded)
				address = location.GetAddress ();
			else if (location.Method.IsDynamic) {
				// A dynamic method is a method which may emit a
				// callback when it's loaded.  We register this
				// callback here and do the actual insertion when
				// the method is loaded.
				load_handler = location.Method.RegisterLoadHandler (
					process, new MethodLoadedHandler (method_loaded),
					null);
			}
		}

		internal static BreakpointHandle Create (Process process, Breakpoint bpt,
							 SourceLocation location)
		{
			return new BreakpointHandle (process, bpt, location);
		}

		internal BreakpointHandle (Breakpoint breakpoint, TargetAddress address)
		{
			this.breakpoint = breakpoint;
			this.address = address;
		}

		public Breakpoint Breakpoint {
			get { return breakpoint; }
		}

		IDisposable load_handler;

		// <summary>
		//   The method has just been loaded, lookup the breakpoint
		//   address and actually insert it.
		// </summary>
		void method_loaded (Inferior inferior, SourceMethod method, object user_data)
		{
			load_handler = null;

			address = location.GetAddress ();
			if (address.IsNull)
				return;

			breakpoint_id = inferior.BreakpointManager.InsertBreakpoint (
				inferior, breakpoint, address);
		}

		TargetAddress address = TargetAddress.Null;
		int breakpoint_id = -1;

		public bool IsEnabled {
			get { return breakpoint_id > 0; }
		}

		public TargetAddress Address {
			get { return address; }
		}

		protected void Enable (Process process)
		{
			lock (this) {
				if ((address.IsNull) || (breakpoint_id > 0))
					return;

				breakpoint_id = process.InsertBreakpoint (breakpoint, address);
			}
		}

		protected void Disable (Process process)
		{
			lock (this) {
				if (breakpoint_id > 0)
					process.RemoveBreakpoint (breakpoint_id);

				breakpoint_id = -1;
			}
		}

		public void EnableBreakpoint (Process process)
		{
			lock (this) {
				Enable (process);
			}
		}

		public void DisableBreakpoint (Process process)
		{
			lock (this) {
				Disable (process);
			}
		}

		public void RemoveBreakpoint (Process process)
		{
			if (load_handler != null) {
				load_handler.Dispose ();
				load_handler = null;
			}
			DisableBreakpoint (process);
		}
	}
}
