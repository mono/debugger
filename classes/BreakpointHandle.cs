using System;
using System.Runtime.Serialization;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	[Serializable]
	public class BreakpointHandle : ISerializable, IDeserializationCallback
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

		public Breakpoint Breakpoint {
			get { return breakpoint; }
		}

		public SourceLocation SourceLocation {
			get { return location; }
		}

		IDisposable load_handler;

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

		TargetAddress address = TargetAddress.Null;
		int breakpoint_id = -1;

		public bool IsEnabled {
			get { return (breakpoint_id > 0) || (load_handler != null); }
		}

		public TargetAddress Address {
			get { return address; }
		}

		protected void Enable (Process process)
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

		protected void Disable (Process process)
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

		//
		// ISerializable
		//

		private Process internal_process;

		public virtual void GetObjectData (SerializationInfo info,
						   StreamingContext context)
		{
			info.AddValue ("location", location);
			info.AddValue ("breakpoint", breakpoint);
			info.AddValue ("enabled", IsEnabled);
		}

		protected BreakpointHandle (SerializationInfo info, StreamingContext context)
		{
			location = (SourceLocation) info.GetValue (
				"location", typeof (SourceLocation));
			breakpoint = (Breakpoint) info.GetValue (
				"breakpoint", typeof (Breakpoint));
			if (info.GetBoolean ("enabled"))
				internal_process = (Process) context.Context;
		}

		void IDeserializationCallback.OnDeserialization (object sender)
		{
			if (internal_process == null)
				return;

			if (location.Method.IsLoaded)
				address = location.GetAddress ();
			EnableBreakpoint (internal_process);
			internal_process = null;
		}
	}
}
