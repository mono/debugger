using System;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public class BreakpointHandle : IEventHandle
	{
		Breakpoint breakpoint;
		SourceLocation location;
		TargetAddress address = TargetAddress.Null;
		int breakpoint_id = -1;
		IDisposable load_handler;

		private BreakpointHandle (Process process, Breakpoint breakpoint,
					  SourceLocation location)
		{
			this.breakpoint = breakpoint;
			this.location = location;

			if (location.Method.IsLoaded)
				address = location.GetAddress ();
			EnableBreakpoint (process);
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

#region IEventHandle
		public Breakpoint Breakpoint {
			get { return breakpoint; }
		}

		public bool IsEnabled {
			get { return (breakpoint_id > 0) || (load_handler != null); }
		}

		public void Enable (Process process)
		{
			lock (this) {
				EnableBreakpoint (process);
			}
		}

		public void Disable (Process process)
		{
			lock (this) {
				DisableBreakpoint (process);
			}
		}

		public void Remove (Process process)
		{
			if (load_handler != null) {
				load_handler.Dispose ();
				load_handler = null;
			}
			Disable (process);
		}
#endregion

		void EnableBreakpoint (Process process)
		{
			lock (this) {
				if ((load_handler != null) || (breakpoint_id > 0))
					return;

				if (!address.IsNull)
					breakpoint_id = process.InsertBreakpoint (
						breakpoint, address);
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
		void method_loaded (ITargetAccess target, SourceMethod method, object data)
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
