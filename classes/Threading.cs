using System;
using System.Threading;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public abstract class DebuggerWaitHandle
	{
		public readonly string Name;
		public DebugFlags DebugFlags = DebugFlags.Mutex;
		protected WaitHandle handle;

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

		public bool TryLock ()
		{
			Debug ("{0} trying to lock {1}", CurrentThread, Name);
			bool success = handle.WaitOne (0, false);
			if (success)
				Debug ("{0} locked {1}", CurrentThread, Name);
			else
				Debug ("{0} could not lock {1}", CurrentThread, Name);
			return success;
		}

		protected void Debug (string format, params object[] args)
		{
			if (((int) DebugFlags & (int) Report.CurrentDebugFlags) == 0)
				return;

			Report.Debug (DebugFlags, format, args);
		}
	}

	public class DebuggerMutex : DebuggerWaitHandle
	{
		Mutex mutex;

		public DebuggerMutex (string name)
			: base (name)
		{
			handle = mutex = new Mutex ();
		}

		public void Lock ()
		{
			Debug ("{0} locking {1}", CurrentThread, Name);
			while (!handle.WaitOne ()) {
				Debug ("{0} still trying to lock {1}", CurrentThread, Name);
			}
			Debug ("{0} locked {1}", CurrentThread, Name);
		}

		public void Unlock ()
		{
			Debug ("{0} unlocking {1}", CurrentThread, Name);
			mutex.ReleaseMutex ();
		}
	}

	public abstract class DebuggerEvent : DebuggerWaitHandle
	{
		protected DebuggerEvent (string name)
			: base (name)
		{ }

		public void Wait ()
		{
			Debug ("{0} waiting for {1}", CurrentThread, Name);
			while (!handle.WaitOne ()) {
				Debug ("{0} still waiting for {1}", CurrentThread, Name);
			}
			Debug ("{0} done waiting for {1}", CurrentThread, Name);
		}

		public void Set ()
		{
			Debug ("{0} signalling {1}", CurrentThread, Name);
			DoSet ();
		}

		protected abstract void DoSet ();
	}

	public class DebuggerManualResetEvent : DebuggerEvent
	{
		ManualResetEvent the_event;

		public DebuggerManualResetEvent (string name, bool initially_locked)
			: base (name)
		{
			handle = the_event = new ManualResetEvent (initially_locked);
		}

		public void Reset ()
		{
			Debug ("{0} resetting {1}", CurrentThread, Name);
			the_event.Reset ();
		}

		protected override void DoSet ()
		{
			the_event.Set ();
		}
	}

	public class DebuggerAutoResetEvent : DebuggerEvent
	{
		AutoResetEvent the_event;

		public DebuggerAutoResetEvent (string name, bool initially_locked)
			: base (name)
		{
			handle = the_event = new AutoResetEvent (initially_locked);
		}

		protected override void DoSet ()
		{
			the_event.Set ();
		}
	}
}
