using GLib;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.CSharp;
using Mono.Debugger.Architecture;

namespace Mono.Debugger
{
	public delegate void ThreadEventHandler (ThreadManager manager, Process process);

	public class ThreadManager : IDisposable
	{
		BfdContainer bfdc;
		DebuggerBackend backend;
		Hashtable thread_hash;
		Process main_process;

		TargetAddress thread_handles = TargetAddress.Null;
		TargetAddress thread_handles_num = TargetAddress.Null;
		TargetAddress last_thread_event = TargetAddress.Null;
		bool initialized = false;

		const int Signal_SIGINT			= 2;

		const int PThread_Signal_Debug		= 34;
		const int PThread_Signal_Restart	= 32;

		internal ThreadManager (DebuggerBackend backend, BfdContainer bfdc)
		{
			this.backend = backend;
			this.bfdc = bfdc;
			this.thread_hash = new Hashtable ();
		}

		public bool Initialize (Process process)
		{
			TargetAddress tdebug = bfdc.LookupSymbol ("__pthread_threads_debug");

			thread_handles = bfdc.LookupSymbol ("__pthread_handles");
			thread_handles_num = bfdc.LookupSymbol ("__pthread_handles_num");
			last_thread_event = bfdc.LookupSymbol ("__pthread_last_event");

			if (tdebug.IsNull || thread_handles.IsNull ||
			    thread_handles_num.IsNull || last_thread_event.IsNull)
				return false;

			main_process = process;
			thread_hash.Add (process.Inferior.PID, process);

			process.Inferior.WriteInteger (tdebug, 1);
			initialized = true;

			Console.WriteLine ("Initialized thread manager.");

			return true;
		}

		public bool Initialized {
			get { return initialized; }
		}

		public DebuggerBackend DebuggerBackend {
			get { return backend; }
		}

		public event ThreadEventHandler ThreadCreatedEvent;
		public event ThreadEventHandler ThreadExitedEvent;

		protected virtual void OnThreadCreatedEvent (Process new_process)
		{
			if (ThreadCreatedEvent != null)
				ThreadCreatedEvent (this, new_process);
		}

		public bool SignalHandler (Process process, int signal, out bool action)
		{
			if (signal == PThread_Signal_Restart) {
				action = false;
				return true;
			}

			if (signal == Signal_SIGINT) {
				process.Inferior.SetSignal (0, false);
				action = true;
				return true;
			}

			if (signal != PThread_Signal_Debug) {
				action = true;
				return false;
			}

			reload_threads (process.Inferior);

			process.Inferior.SetSignal (PThread_Signal_Restart, false);
			action = false;
			return true;
		}

		void reload_threads (ITargetMemoryAccess memory)
		{
			int size = memory.TargetIntegerSize * 2 + memory.TargetAddressSize * 2;
			int offset = memory.TargetIntegerSize * 2;

			int count = memory.ReadInteger (thread_handles_num);
			for (int index = 0; index <= count; index++) {
				TargetAddress thandle_addr = thread_handles + index * size + offset;

				TargetAddress thandle = memory.ReadAddress (thandle_addr);

				if (thandle.IsNull || (thandle.Address == 0))
					continue;

				thandle += 20 * memory.TargetAddressSize;
				int tid = memory.ReadInteger (thandle);
				thandle += memory.TargetIntegerSize;
				int pid = memory.ReadInteger (thandle);

				if (thread_hash.Contains (pid))
					continue;

				Process new_process = main_process.CreateThread (pid);
				thread_hash.Add (pid, new_process);

				if (index == 1) {
					new_process.Inferior.SetSignal (PThread_Signal_Restart, false);
					new_process.SingleSteppingEngine.Continue (true);
				} else
					new_process.Inferior.SetSignal (PThread_Signal_Restart, true);

				OnThreadCreatedEvent (new_process);
			}
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("ThreadManager");
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
					// Nothing to do yet.
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~ThreadManager ()
		{
			Dispose (false);
		}
	}
}
