using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

using Mono.Debugger;

namespace Mono.Debugger.Backends
{
	internal abstract class NativeModule : Module
	{
		IInferior inferior;
		DebuggerBackend backend;

		public NativeModule (string name, DebuggerBackend backend)
			: base (name)
		{
			this.backend = backend;
		}

		public IInferior Inferior {
			get {
				return inferior;
			}

			set {
				inferior = value;
				CheckLoaded ();
			}
		}

		public override bool IsLoaded {
			get {
				return Inferior != null;
			}
		}

		public override void UnLoad ()
		{
			Inferior = null;
		}

		protected override void AddBreakpoint (BreakpointHandle handle)
		{ }

		protected override void RemoveBreakpoint (BreakpointHandle handle)
		{ }

		bool breakpoint_hit (StackFrame frame, int index, object user_data)
		{
			BreakpointHandle handle = (BreakpointHandle) user_data;

			return handle.Breakpoint.BreakpointHit (frame);
		}

		protected override object EnableBreakpoint (BreakpointHandle handle, TargetAddress address)
		{
			if (!IsLoaded)
				return null;

			return backend.SingleSteppingEngine.InsertBreakpoint (
				address, new BreakpointHitHandler (breakpoint_hit), handle);
		}

		protected override void DisableBreakpoint (BreakpointHandle handle, object data)
		{
			if (!IsLoaded)
				return;

			backend.SingleSteppingEngine.RemoveBreakpoint ((int) data);
		}

		//
		// ISerializable
		//

		public override void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			base.GetObjectData (info, context);
		}

		private NativeModule (SerializationInfo info, StreamingContext context)
			: base (info, context)
		{ }
	}
}
