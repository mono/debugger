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
							    TargetAddress address)
		{
			throw new NotImplementedException ();
		}

		protected override void DisableBreakpoint (BreakpointHandle handle, object data)
		{
			throw new NotImplementedException ();
		}
	}
}
