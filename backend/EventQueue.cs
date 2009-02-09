using System;
using System.Threading;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Backend
{
	internal abstract class DebuggerWaitHandle : IDisposable
	{
		public readonly string Name;
		public DebugFlags DebugFlags = DebugFlags.Mutex;

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_get_current_pid ();

		[DllImport("monodebuggerserver")]
		static extern long mono_debugger_server_get_current_thread ();

		protected DebuggerWaitHandle (string name)
		{
			this.Name = name;
		}

		public static string CurrentThread {
			get {
				int pid = mono_debugger_server_get_current_pid ();
				long thread = mono_debugger_server_get_current_thread ();
				return String.Format ("[{0}:{1}:0x{2:x}]",
						      Environment.MachineName, pid, thread);
			}
		}

		public abstract bool TryLock ();

		protected void Debug (string format, params object[] args)
		{
			Report.Debug (DebugFlags, format, args);
		}

#region IDisposable implementation
		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("DebuggerWaitHandle");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				DoDispose ();
				disposed = true;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		protected abstract void DoDispose ();

		~DebuggerWaitHandle ()
		{
			Dispose (false);
		}
#endregion
	}

	internal class DebuggerMutex : DebuggerWaitHandle
	{
		protected IntPtr handle;

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_mutex_new ();

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_mutex_free (IntPtr handle);

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

		protected override void DoDispose ()
		{
			mono_debugger_mutex_free (handle);
			handle = IntPtr.Zero;
		}
	}

	internal class DebuggerEventQueue : DebuggerMutex
	{
		IntPtr cond;

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_cond_new ();

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_cond_free (IntPtr handle);

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

		protected override void DoDispose ()
		{
			mono_debugger_cond_free (cond);
			cond = IntPtr.Zero;
		}
	}
}
