using System;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Backends
{
	public static class Semaphore
	{
		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_sem_init ();

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_sem_wait ();

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_sem_post ();

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_sem_get_value ();

		static Semaphore ()
		{
			mono_debugger_server_sem_init ();
		}

		public static void Wait ()
		{
			Report.Debug (DebugFlags.Mutex, "{0} waiting for semaphore",
				      DebuggerWaitHandle.CurrentThread);
			mono_debugger_server_sem_wait ();
			Report.Debug (DebugFlags.Mutex, "{0} done waiting for semaphore",
				      DebuggerWaitHandle.CurrentThread);
		}

		public static void Set ()
		{
			Report.Debug (DebugFlags.Mutex, "{0} signalling semaphore",
				      DebuggerWaitHandle.CurrentThread);
			mono_debugger_server_sem_post ();
		}

		public static int Value {
			get {
				return mono_debugger_server_sem_get_value ();
			}
		}
	}
}
