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

namespace Mono.Debugger
{
	public delegate void ThreadEventHandler (Debugger debugger, Thread thread);
	public delegate void ProcessEventHandler (Debugger debugger, Process process);

	public class Debugger : MarshalByRefObject, IDisposable
	{
		ThreadManager thread_manager;
		DebuggerSession session;

		public Debugger ()
		{
			session = new DebuggerSession (this);
			thread_manager = new ThreadManager (this);
		}

		internal ThreadManager ThreadManager {
			get {
				return thread_manager;
			}
		}

		public DebuggerSession Session {
			get { return session; }
		}

		public event ThreadEventHandler ThreadCreatedEvent;
		public event ThreadEventHandler ThreadExitedEvent;
		public event ProcessEventHandler ProcessCreatedEvent;
		public event ProcessEventHandler ProcessExitedEvent;
		public event TargetEventHandler TargetEvent;
		public event SymbolTableChangedHandler SymbolTableChanged;

		internal void OnProcessCreatedEvent (Process process)
		{
			if (ProcessCreatedEvent != null)
				ProcessCreatedEvent (this, process);
		}

		internal void OnProcessExitedEvent (Process process)
		{
			if (ProcessExitedEvent != null)
				ProcessExitedEvent (this, process);
		}

		public Process Run (DebuggerOptions options)
		{
			check_disposed ();

			ProcessStart start = new ProcessStart (options);
			return thread_manager.StartApplication (start);
		}

		public Process Attach (DebuggerOptions options, int pid)
		{
			check_disposed ();

			ProcessStart start = new ProcessStart (options, pid);
			return thread_manager.StartApplication (start);
		}

		public Process OpenCoreFile (DebuggerOptions options, string core_file,
					     out Thread[] threads)
		{
			check_disposed ();

			ProcessStart start = new ProcessStart (options, core_file);
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
