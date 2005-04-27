using System;
using System.Threading;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public abstract class DebuggerWaitHandle
	{
		public readonly string Name;
		public DebugFlags DebugFlags = DebugFlags.Mutex;

		[DllImport("monodebuggerserver")]
		static extern long mono_debugger_server_get_current_thread ();

		protected DebuggerWaitHandle (string name)
		{
			this.Name = name;
		}

		public static string CurrentThread {
			get {
				long thread = mono_debugger_server_get_current_thread ();
				return String.Format ("0x{0:x}", thread);
			}
		}

		public abstract bool TryLock ();

		protected void Debug (string format, params object[] args)
		{
			if (((int) DebugFlags & (int) Report.CurrentDebugFlags) == 0)
				return;

			Report.Debug (DebugFlags, format, args);
		}
	}

	public class DebuggerMutex : DebuggerWaitHandle
	{
		new IntPtr handle;

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_mutex_new ();

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_mutex_lock (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_mutex_unlock (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern bool mono_debugger_mutex_trylock (IntPtr handle);

		public DebuggerMutex (string name)
			: base (name)
		{
			handle = mono_debugger_mutex_new ();
		}

		public void Lock ()
		{
			Debug ("{0} locking {1}", CurrentThread, Name);
			mono_debugger_mutex_lock (handle);
			Debug ("{0} locked {1}", CurrentThread, Name);
		}

		public void Unlock ()
		{
			Debug ("{0} unlocking {1}", CurrentThread, Name);
			mono_debugger_mutex_unlock (handle);
			Debug ("{0} unlocked {1}", CurrentThread, Name);
		}

		public override bool TryLock ()
		{
			Debug ("{0} trying to lock {1}", CurrentThread, Name);
			bool success = mono_debugger_mutex_trylock (handle);
			if (success)
				Debug ("{0} locked {1}", CurrentThread, Name);
			else
				Debug ("{0} could not lock {1}", CurrentThread, Name);
			return success;
		}
	}

}
