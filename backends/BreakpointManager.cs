using System;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Backends
{
	internal class BreakpointManager : IDisposable
	{
		IntPtr _manager;

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_new ();

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_breakpoint_manager_free (IntPtr manager);

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_breakpoint_manager_lookup (IntPtr manager, long address);

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_breakpoint_info_get_id (IntPtr info);

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_breakpoint_info_get_owner (IntPtr info);

		public BreakpointManager ()
		{
			_manager = mono_debugger_breakpoint_manager_new ();
		}

		internal IntPtr Manager {
			get { return _manager; }
		}

		public int LookupBreakpoint (TargetAddress address, out int owner)
		{
			IntPtr info = mono_debugger_breakpoint_manager_lookup (_manager, address.Address);
			if (info == IntPtr.Zero) {
				owner = 0;
				return 0;
			}

			owner = mono_debugger_breakpoint_info_get_owner (info);
			return mono_debugger_breakpoint_info_get_id (info);
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("BreakpointManager");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					// Do stuff here
				}
				
				// Release unmanaged resources
				this.disposed = true;

				lock (this) {
					mono_debugger_breakpoint_manager_free (_manager);
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~BreakpointManager ()
		{
			Dispose (false);
		}
	}
}
