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
				if (inferior == value)
					return;
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
			base.UnLoad ();
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

		protected override object EnableBreakpoint (BreakpointHandle handle,
							    ThreadGroup group, TargetAddress address)
		{
			if (!IsLoaded)
				return null;

			Hashtable hash = new Hashtable ();
			foreach (IProcess thread in group.Threads) {
				Process process = thread as Process;
				if (process == null)
					throw new NotSupportedException ();

				int id = process.SingleSteppingEngine.InsertBreakpoint (
					address, new BreakpointHitHandler (breakpoint_hit),
					handle.Breakpoint.HandlerNeedsFrame, handle);
				hash.Add (process, id);
			}

			return hash;
		}

		protected override void DisableBreakpoint (BreakpointHandle handle,
							   ThreadGroup group, object data)
		{
			if (!IsLoaded)
				return;

			Hashtable hash = (Hashtable) data;

			foreach (IProcess thread in group.Threads) {
				Process process = thread as Process;
				if (process == null)
					throw new NotSupportedException ();
				if (!hash.Contains (process))
					throw new NotSupportedException ();

				int id = (int) hash [process];
				process.SingleSteppingEngine.RemoveBreakpoint (id);
				hash.Remove (id);
			}
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
