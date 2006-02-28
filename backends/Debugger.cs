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
using System.Runtime.Remoting;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;
using Mono.Debugger.Remoting;

namespace Mono.Debugger
{
	public delegate void DebuggerEventHandler (Debugger debugger, Thread thread);
	public delegate void ProcessEventHandler (Debugger debugger, Process process);

	public abstract class Debugger : MarshalByRefObject, IDisposable
	{
		DebuggerClient client;
		ThreadManager thread_manager;
		ProcessStart start;

		protected Debugger (DebuggerClient client)
		{
			this.client = client;

			thread_manager = new ThreadManager (this);
		}

		internal ThreadManager ThreadManager {
			get {
				return thread_manager;
			}
		}

		internal ProcessStart ProcessStart {
			get {
				return start;
			}
		}

		public event DebuggerEventHandler ThreadCreatedEvent;
		public event DebuggerEventHandler ThreadExitedEvent;
		public event ProcessEventHandler InitializedEvent;
		public event TargetExitedHandler TargetExitedEvent;
		public event TargetEventHandler TargetEvent;
		public event SymbolTableChangedHandler SymbolTableChanged;

		internal void OnInitializedEvent (Process process)
		{
			process.MainThreadGroup.AddThread (process.MainThread.ID);
			if (InitializedEvent != null)
				InitializedEvent (this, process);
		}

		public Process Run (DebuggerOptions options)
		{
			check_disposed ();

#if FIXME
			if (thread_manager.HasTarget)
				throw new TargetException (TargetError.AlreadyHaveTarget);
#endif

			start = new ProcessStart (options);

			return thread_manager.StartApplication (start);
		}

		public Process Attach (DebuggerOptions options, int pid)
		{
			check_disposed ();

			if (thread_manager.HasTarget)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			start = new ProcessStart (options, pid);

			return thread_manager.StartApplication (start);
		}

		public Process OpenCoreFile (DebuggerOptions options, string core_file,
					     out Thread[] threads)
		{
			check_disposed ();

			if (thread_manager.HasTarget)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			start = new ProcessStart (options, core_file);

			return thread_manager.OpenCoreFile (start, out threads);
		}

		internal void OnThreadCreatedEvent (Thread new_process)
		{
			if (ThreadCreatedEvent != null)
				ThreadCreatedEvent (this, new_process);
		}

		internal void OnThreadExitedEvent (Thread thread)
		{
			if (ThreadExitedEvent != null)
				ThreadExitedEvent (this, thread);
		}

		internal void OnTargetExitedEvent ()
		{
			if (TargetExitedEvent != null)
				TargetExitedEvent ();
		}

		internal void SendTargetEvent (SingleSteppingEngine sse, TargetEventArgs args)
		{
			try {
				if (TargetEvent != null)
					TargetEvent (sse.Thread, args);
			} catch (Exception ex) {
				Error ("{0} caught exception while sending {1}:\n{2}",
				       sse, args, ex);
			}
		}

		public void Error (string message, params object[] args)
		{
			Console.WriteLine ("ERROR: " + String.Format (message, args));
		}

		//
		// IDisposable
		//

		protected abstract void DebuggerExited ();

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Debugger");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				if (thread_manager != null) {
					thread_manager.Dispose ();
					thread_manager = null;
				}

				ObjectCache.Shutdown ();

				DebuggerExited ();
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Debugger ()
		{
			Dispose (false);
		}
	}
}
