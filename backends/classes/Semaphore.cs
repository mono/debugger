using System;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Backends
{
	internal class Semaphore
	{
		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_sem_init ();

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_sem_wait ();

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_server_sem_post ();

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_server_sem_get_value ();

		protected Semaphore ()
		{
			mono_debugger_server_sem_init ();
		}

		public static Semaphore CreateThreadManagerSemaphore ()
		{
			return new Semaphore ();
		}

		public void Wait ()
		{
			mono_debugger_server_sem_wait ();
		}

		public void Set ()
		{
			mono_debugger_server_sem_post ();
		}

		public int Value {
			get {
				return mono_debugger_server_sem_get_value ();
			}
		}

		public override string ToString ()
		{
			return String.Format ("Semaphore ({0})", Value);
		}
	}
}
