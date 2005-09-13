using System;
using System.Threading;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Backends
{
	public class DebuggerEventQueue : DebuggerMutex
	{
		IntPtr cond;

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_cond_new ();

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_cond_wait (IntPtr mutex, IntPtr cond);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_cond_broadcast (IntPtr cond);

		public DebuggerEventQueue (string name)
			: base (name)
		{
			cond = mono_debugger_cond_new ();
		}

		public void Wait ()
		{
			Debug ("{0} waiting {1}", CurrentThread, Name);
			mono_debugger_cond_wait (handle, cond);
			Debug ("{0} done waiting {1}", CurrentThread, Name);
		}

		public void Signal ()
		{
			Debug ("{0} signal {1}", CurrentThread, Name);
			mono_debugger_cond_broadcast (cond);
		}
	}

}
