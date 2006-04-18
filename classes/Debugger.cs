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
	public delegate void DebuggerEventHandler (Debugger debugger);
	public delegate void ThreadEventHandler (Debugger debugger, Thread thread);
	public delegate void ProcessEventHandler (Debugger debugger, Process process);

	public class Debugger : MarshalByRefObject, IDisposable
	{
		ThreadManager thread_manager;
		Hashtable process_hash;
		Process main_process;

		public Debugger ()
		{
			thread_manager = new ThreadManager (this);
			process_hash = Hashtable.Synchronized (new Hashtable ());
		}

		internal ThreadManager ThreadManager {
			get {
				return thread_manager;
			}
		}

		public event TargetOutputHandler TargetOutputEvent;
		public event ThreadEventHandler ThreadCreatedEvent;
		public event ThreadEventHandler ThreadExitedEvent;
		public event ProcessEventHandler ProcessCreatedEvent;
		public event ProcessEventHandler ProcessExitedEvent;
		public event ProcessEventHandler ProcessExecdEvent;
		public event DebuggerEventHandler TargetExitedEvent;
		public event TargetEventHandler TargetEvent;
		public event SymbolTableChangedHandler SymbolTableChanged;

		internal void OnProcessCreatedEvent (Process process)
		{
			process_hash.Add (process, process);
			if (ProcessCreatedEvent != null)
				ProcessCreatedEvent (this, process);
		}

		protected void OnTargetExitedEvent ()
		{
			if (TargetExitedEvent != null)
				TargetExitedEvent (this);
		}

		internal void OnProcessExitedEvent (Process process)
		{
			process_hash.Remove (process);
			if (ProcessExitedEvent != null)
				ProcessExitedEvent (this, process);

			if (process == main_process) {
				Kill ();
			}
		}

		internal void OnProcessExecdEvent (Process process)
		{
			if (ProcessExecdEvent != null)
				ProcessExecdEvent (this, process);
		}

		public void Kill ()
		{
			main_process = null;

			Process[] procs;
			lock (process_hash.SyncRoot) {
				procs = new Process [process_hash.Count];
				process_hash.Values.CopyTo (procs, 0);
			}

			foreach (Process proc in procs) {
				proc.Kill ();
			}

			OnTargetExitedEvent ();
		}

		public Process Run (DebuggerOptions options)
		{
			check_disposed ();

			if (main_process != null)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			ProcessStart start = new ProcessStart (options);
			main_process = thread_manager.StartApplication (start);
			process_hash.Add (main_process, main_process);
			return main_process;
		}

		public Process Attach (DebuggerOptions options, int pid)
		{
			check_disposed ();

			if (main_process != null)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			ProcessStart start = new ProcessStart (options, pid);
			main_process = thread_manager.StartApplication (start);
			process_hash.Add (main_process, main_process);
			return main_process;
		}

		public Process OpenCoreFile (DebuggerOptions options, string core_file,
					     out Thread[] threads)
		{
			check_disposed ();

			if (main_process != null)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			ProcessStart start = new ProcessStart (options, core_file);
			main_process = thread_manager.OpenCoreFile (start, out threads);
			process_hash.Add (main_process, main_process);
			return main_process;
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

		internal void OnInferiorOutput (bool is_stderr, string line)
		{
			if (TargetOutputEvent != null)
				TargetOutputEvent (is_stderr, line);
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

		public Process[] Processes {
			get {
				lock (process_hash.SyncRoot) {
					Process[] procs = new Process [process_hash.Count];
					process_hash.Values.CopyTo (procs, 0);
					return procs;
				}
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
