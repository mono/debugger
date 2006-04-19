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

namespace Mono.Debugger.Backends
{
	internal class DebuggerServant : MarshalByRefObject, IDisposable
	{
		Debugger client;
		ThreadManager thread_manager;
		Hashtable process_hash;
		Process main_process;

		internal DebuggerServant (Debugger client)
		{
			this.client = client;
			thread_manager = new ThreadManager (this);
			process_hash = Hashtable.Synchronized (new Hashtable ());
		}

		public Debugger Client {
			get { return client; }
		}

		internal ThreadManager ThreadManager {
			get { return thread_manager; }
		}

		internal void OnProcessCreatedEvent (Process process)
		{
			process_hash.Add (process, process);
			client.OnProcessCreatedEvent (process);
		}

		protected void OnTargetExitedEvent ()
		{
			client.OnTargetExitedEvent ();
		}

		internal void OnProcessExitedEvent (Process process)
		{
			process_hash.Remove (process);
			client.OnProcessExitedEvent (process);

			if (process == main_process) {
				Kill ();
			}
		}

		internal void OnProcessExecdEvent (Process process)
		{
			client.OnProcessExecdEvent (process);
		}

		internal void OnThreadCreatedEvent (Thread thread)
		{
			client.OnThreadCreatedEvent (thread);
		}

		internal void OnThreadExitedEvent (Thread thread)
		{
			client.OnThreadExitedEvent (thread);
		}

		internal void OnInferiorOutput (bool is_stderr, string line)
		{
			client.OnInferiorOutput (is_stderr, line);
		}

		internal void SendTargetEvent (SingleSteppingEngine sse, TargetEventArgs args)
		{
			try {
				client.OnTargetEvent (sse.Thread, args);
			} catch (Exception ex) {
				Error ("{0} caught exception while sending {1}:\n{2}",
				       sse, args, ex);
			}
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

		public void Detach ()
		{
			if (main_process == null)
				throw new TargetException (TargetError.NoTarget);
			else if (!main_process.IsAttached)
				throw new TargetException (TargetError.CannotDetach);

			main_process = null;

			Process[] procs;
			lock (process_hash.SyncRoot) {
				procs = new Process [process_hash.Count];
				process_hash.Values.CopyTo (procs, 0);
			}

			foreach (Process proc in procs) {
				proc.Detach ();
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

		~DebuggerServant ()
		{
			Dispose (false);
		}
	}
}
