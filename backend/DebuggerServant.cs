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

using Mono.Debugger.Backend;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger.Backend
{
	internal class DebuggerServant : DebuggerMarshalByRefObject, IOperationHost, IDisposable
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
			Report.Initialize (writer);
			ObjectCache.Initialize ();
			thread_manager = new ThreadManager (this);
			process_hash = Hashtable.Synchronized (new Hashtable ());
			stopped_event = new ManualResetEvent (false);
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

		internal void OnModuleLoaded (Module module)
		{
			client.OnModuleLoadedEvent (module);
		}

		internal void OnModuleUnLoaded (Module module)
		{
			client.OnModuleUnLoadedEvent (module);
		}

		internal void OnMainProcessCreatedEvent (ProcessServant process)
		{
			process_hash.Add (process, process);
			client.OnMainProcessCreatedEvent (process.Client);
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
			client.OnThreadCreatedEvent (thread);
		}

		internal void OnThreadExitedEvent (Thread thread)
		{
			client.OnThreadExitedEvent (thread);
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

		internal void OnEnterNestedBreakState (SingleSteppingEngine sse)
		{
			client.OnEnterNestedBreakState (sse.Thread);
		}

		internal void OnLeaveNestedBreakState (SingleSteppingEngine sse)
		{
			client.OnLeaveNestedBreakState (sse.Thread);
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

		public bool CanDetach {
			get {
				return (main_process != null) && main_process.CanDetach;
			}
		}

		public void Detach ()
		{
			if (main_process == null)
				throw new TargetException (TargetError.NoTarget);
			else if (!main_process.CanDetach)
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

		public Process Run (DebuggerSession session, out CommandResult result)
		{
			check_disposed ();

			if (main_process != null)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			ProcessStart start = new ProcessStart (session);
			main_process = thread_manager.StartApplication (start, out result);
			return main_process.Client;
		}

		public Process Attach (DebuggerSession session, int pid, out CommandResult result)
		{
			check_disposed ();

			if (main_process != null)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			ProcessStart start = new ProcessStart (session, pid);
			main_process = thread_manager.StartApplication (start, out result);
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

#region Global Threading Model

		ManualResetEvent stopped_event;
		OperationCommandResult current_operation;

		internal WaitHandle WaitHandle {
			get { return stopped_event; }
		}

		internal CommandResult StartOperation (ThreadingModel model, SingleSteppingEngine caller)
		{
			if (!ThreadManager.InBackgroundThread)
				throw new InternalError ();

			if ((model & ThreadingModel.ThreadingMode) == ThreadingModel.Default) {
				if (Inferior.HasThreadEvents)
					model |= ThreadingModel.Single;
				else
					model |= ThreadingModel.Process;
			}

			if ((model & ThreadingModel.ThreadingMode) != ThreadingModel.Global)
				return caller.Process.StartOperation (model, caller);

			if (current_operation != null)
				throw new TargetException (TargetError.NotStopped);

			lock (this) {
				stopped_event.Reset ();
				current_operation = new GlobalCommandResult (this, model);
			}

			foreach (ProcessServant process in process_hash.Values) {
				process.StartGlobalOperation (model, caller, current_operation);
			}

			return current_operation;
		}

		public void OperationCompleted (SingleSteppingEngine caller, TargetEventArgs result, ThreadingModel model)
		{
			if (!ThreadManager.InBackgroundThread)
				throw new InternalError ();

			foreach (ProcessServant process in process_hash.Values) {
				process.OperationCompleted (caller, result, model);
			}

			lock (this) {
				current_operation = null;
				stopped_event.Set ();
			}
		}

		WaitHandle IOperationHost.WaitHandle {
			get { return stopped_event; }
		}

		void IOperationHost.SendResult (SingleSteppingEngine sse, TargetEventArgs args)
		{
			SendTargetEvent (sse, args);
		}

		void IOperationHost.Abort ()
		{
			foreach (ProcessServant process in process_hash.Values) {
				process.Stop ();
			}
		}

		protected class GlobalCommandResult : OperationCommandResult
		{
			public DebuggerServant Debugger {
				get; private set;
			}

			internal override IOperationHost Host {
				get { return Debugger; }
			}

			internal GlobalCommandResult (DebuggerServant debugger, ThreadingModel model)
				: base (model)
			{
				this.Debugger = debugger;
			}

			internal override void OnExecd (SingleSteppingEngine new_thread)
			{ }
		}

#endregion

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
