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
	internal class DebuggerServant : DebuggerMarshalByRefObject, IDisposable
	{
		Debugger client;
		DebuggerConfiguration config;
		ThreadManager thread_manager;
		Hashtable process_hash;
		ProcessServant main_process;

		internal DebuggerServant (Debugger client, ReportWriter writer,
					  DebuggerConfiguration config)
		{
			this.client = client;
			this.config = config;
			Report.ReportWriter = writer;
			ObjectCache.Initialize ();
			thread_manager = new ThreadManager (this);
			process_hash = Hashtable.Synchronized (new Hashtable ());
		}

		public Debugger Client {
			get { return client; }
		}

		public DebuggerConfiguration Configuration {
			get { return config; }
		}

		internal ThreadManager ThreadManager {
			get { return thread_manager; }
		}

		internal void OnProcessCreatedEvent (ProcessServant process)
		{
			process_hash.Add (process, process);
			client.OnProcessCreatedEvent (process.Client);
		}

		internal void OnTargetExitedEvent ()
		{
			client.OnTargetExitedEvent ();
		}

		internal void OnProcessExitedEvent (ProcessServant process)
		{
			process_hash.Remove (process);
			client.OnProcessExitedEvent (process.Client);

			if (process_hash.Count == 0)
				OnTargetExitedEvent ();
		}

		internal void OnProcessExecdEvent (ProcessServant process)
		{
			client.OnProcessExecdEvent (process.Client);
		}

		internal void OnThreadCreatedEvent (Thread thread)
		{
			if (!thread.IsDaemon)
				client.OnThreadCreatedEvent (thread);
		}

		internal void OnThreadExitedEvent (Thread thread)
		{
			if (!thread.IsDaemon)
				client.OnThreadExitedEvent (thread);
		}

		internal void OnInferiorOutput (bool is_stderr, string line)
		{
			client.OnInferiorOutput (is_stderr, line);
		}

		internal void SendTargetEvent (SingleSteppingEngine sse, TargetEventArgs args)
		{
			try {
				if (sse.Thread.IsDaemon &&
				    ((args.Type == TargetEventType.TargetExited) ||
				     (args.Type == TargetEventType.TargetSignaled)))
					return;
				client.OnTargetEvent (sse.Thread, args);
			} catch (Exception ex) {
				Error ("{0} caught exception while sending {1}:\n{2}",
				       sse, args, ex);
			}
		}

		public void Kill ()
		{
			main_process = null;

			ProcessServant[] procs;
			lock (process_hash.SyncRoot) {
				procs = new ProcessServant [process_hash.Count];
				process_hash.Values.CopyTo (procs, 0);
			}

			foreach (ProcessServant proc in procs) {
				proc.Kill ();
			}
		}

		public void Detach ()
		{
			if (main_process == null)
				throw new TargetException (TargetError.NoTarget);
			else if (!main_process.IsAttached)
				throw new TargetException (TargetError.CannotDetach);

			ProcessServant[] procs;
			lock (process_hash.SyncRoot) {
				procs = new ProcessServant [process_hash.Count];
				process_hash.Values.CopyTo (procs, 0);
			}

			foreach (ProcessServant proc in procs) {
				proc.Detach ();
			}
		}

		public Process Run (DebuggerSession session)
		{
			check_disposed ();

			if (main_process != null)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			ProcessStart start = new ProcessStart (session);
			main_process = thread_manager.StartApplication (start);
			process_hash.Add (main_process, main_process);
			return main_process.Client;
		}

		public Process Attach (DebuggerSession session, int pid)
		{
			check_disposed ();

			if (main_process != null)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			ProcessStart start = new ProcessStart (session, pid);
			main_process = thread_manager.StartApplication (start);
			process_hash.Add (main_process, main_process);
			return main_process.Client;
		}

		public Process OpenCoreFile (DebuggerSession session, string core_file,
					     out Thread[] threads)
		{
			check_disposed ();

			if (main_process != null)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			ProcessStart start = new ProcessStart (session, core_file);

			main_process = thread_manager.OpenCoreFile (start, out threads);
			process_hash.Add (main_process, main_process);
			return main_process.Client;
		}

		public Process[] Processes {
			get {
				lock (process_hash.SyncRoot) {
					int count = process_hash.Count;
					Process[] procs = new Process [count];
					ProcessServant[] servants = new ProcessServant [count];
					process_hash.Values.CopyTo (servants, 0);
					for (int i = 0; i < count; i++)
						procs [i] = servants [i].Client;
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
