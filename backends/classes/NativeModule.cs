using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

using Mono.Debugger;

namespace Mono.Debugger.Backends
{
	internal abstract class NativeModule : ModuleData
	{
		DebuggerBackend backend;

		public NativeModule (DebuggerBackend backend, Module module, string name)
			: base (module, name)
		{
			this.backend = backend;
		}

		bool check_breakpoint_hit (StackFrame frame, int index, object user_data)
		{
			BreakpointHandle handle = (BreakpointHandle) user_data;

			return handle.Breakpoint.CheckBreakpointHit (frame);
		}

		void breakpoint_hit (StackFrame frame, int index, object user_data)
		{
			BreakpointHandle handle = (BreakpointHandle) user_data;

			handle.Breakpoint.BreakpointHit (frame);
		}

		protected override object EnableBreakpoint (BreakpointHandle handle,
							    ThreadGroup group, TargetAddress address)
		{
			Hashtable hash = new Hashtable ();
			foreach (IProcess thread in group.Threads) {
				Process process = thread as Process;
				if (process == null)
					throw new NotSupportedException ();

				int id = process.SingleSteppingEngine.InsertBreakpoint (
					address, new BreakpointCheckHandler (check_breakpoint_hit),
					new BreakpointHitHandler (breakpoint_hit),
					handle.Breakpoint.HandlerNeedsFrame, handle);
				hash.Add (process, id);
			}

			return hash;
		}

		protected override void DisableBreakpoint (BreakpointHandle handle,
							   ThreadGroup group, object data)
		{
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
	}
}
