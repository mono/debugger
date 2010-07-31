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

namespace Mono.Debugger
{
	public delegate void DebuggerEventHandler (Debugger debugger);
	public delegate void ThreadEventHandler (Debugger debugger, Thread thread);
	public delegate void ProcessEventHandler (Debugger debugger, Process process);

	public class Debugger : DebuggerMarshalByRefObject
	{
		ManualResetEvent kill_event;
		DebuggerConfiguration config;
		ThreadManager thread_manager;
		Hashtable process_hash;
		ProcessServant main_process;
		MyOperationHost operation_host;
		bool alive;

		public Debugger (DebuggerConfiguration config)
		{
			this.config = config;
			this.alive = true;

			ObjectCache.Initialize ();

			kill_event = new ManualResetEvent (false);

			thread_manager = new ThreadManager (this);
			process_hash = Hashtable.Synchronized (new Hashtable ());
			stopped_event = new ManualResetEvent (false);
			operation_host = new MyOperationHost (this);
		}

		public DebuggerConfiguration Configuration {
			get { return config; }
		}

		internal IOperationHost OperationHost {
			get { return operation_host; }
		}

		internal ThreadManager ThreadManager {
			get { return thread_manager; }
		}

		public event ThreadEventHandler ThreadCreatedEvent;
		public event ThreadEventHandler ThreadExitedEvent;
		public event ThreadEventHandler ManagedThreadCreatedEvent;
		public event ProcessEventHandler MainProcessCreatedEvent;
		public event ProcessEventHandler ProcessReachedMainEvent;
		public event ProcessEventHandler ProcessCreatedEvent;
		public event ProcessEventHandler ProcessExitedEvent;
		public event ProcessEventHandler ProcessExecdEvent;
		public event ModuleEventHandler ModuleLoadedEvent;
		public event ModuleEventHandler ModuleUnLoadedEvent;
		public event DebuggerEventHandler TargetExitedEvent;
		public event TargetEventHandler TargetEvent;
		public event SymbolTableChangedHandler SymbolTableChanged;

		public event ThreadEventHandler EnterNestedBreakStateEvent;
		public event ThreadEventHandler LeaveNestedBreakStateEvent;

		internal Process CreateProcess (ProcessServant servant)
		{
			return new Process (this, servant);
		}

		internal Thread CreateThread (ThreadServant servant, int id)
		{
			return new Thread (servant, id);
		}

		internal void OnMainProcessCreatedEvent (ProcessServant process)
		{
			process_hash.Add (process, process);
			if (MainProcessCreatedEvent != null)
				MainProcessCreatedEvent (this, process.Client);
		}

		internal void OnProcessCreatedEvent (ProcessServant process)
		{
			process_hash.Add (process, process);
			if (ProcessCreatedEvent != null)
				ProcessCreatedEvent (this, process.Client);
		}

		internal void OnProcessExitedEvent (ProcessServant process)
		{
			process_hash.Remove (process);
			if (ProcessExitedEvent != null)
				ProcessExitedEvent (this, process.Client);

			if (process_hash.Count == 0)
				OnTargetExitedEvent ();
		}

		internal void OnProcessReachedMainEvent (Process process)
		{
			if (ProcessReachedMainEvent != null)
				ProcessReachedMainEvent (this, process);
		}

		internal void OnTargetExitedEvent ()
		{
			ThreadPool.QueueUserWorkItem (delegate {
				Dispose ();
				if (TargetExitedEvent != null)
					TargetExitedEvent (this);
				kill_event.Set ();
			});
		}

		internal void OnProcessExecdEvent (Process process)
		{
			if (ProcessExecdEvent != null)
				ProcessExecdEvent (this, process);
		}

		internal void OnThreadCreatedEvent (Thread new_process)
		{
			if (ThreadCreatedEvent != null)
				ThreadCreatedEvent (this, new_process);
		}

		internal void OnManagedThreadCreatedEvent (Thread new_thread)
		{
			if (ManagedThreadCreatedEvent != null)
				ManagedThreadCreatedEvent (this, new_thread);
		}

		internal void OnThreadExitedEvent (Thread thread)
		{
			if (ThreadExitedEvent != null)
				ThreadExitedEvent (this, thread);
		}

		internal void OnTargetEvent (Thread thread, TargetEventArgs args)
		{
			try {
				if (TargetEvent != null)
					TargetEvent (thread, args);
			} catch (Exception ex) {
				Error ("{0} caught exception while sending {1}:\n{2}",
				       thread, args, ex);
			}
		}

		internal void OnModuleLoadedEvent (Module module)
		{
			if (ModuleLoadedEvent != null)
				ModuleLoadedEvent (module);
		}

		internal void OnModuleUnLoadedEvent (Module module)
		{
			if (ModuleUnLoadedEvent != null)
				ModuleUnLoadedEvent (module);
		}

		internal void OnEnterNestedBreakState (Thread thread)
		{
			if (EnterNestedBreakStateEvent != null)
				EnterNestedBreakStateEvent (this, thread);
		}

		internal void OnLeaveNestedBreakState (Thread thread)
		{
			if (LeaveNestedBreakStateEvent != null)
				LeaveNestedBreakStateEvent (this, thread);
		}

		public void Error (string message, params object[] args)
		{
			Console.WriteLine ("ERROR: " + String.Format (message, args));
		}

		public void Kill ()
		{
			if (!alive)
				return;

			main_process = null;

			ProcessServant[] procs;
			lock (process_hash.SyncRoot) {
				procs = new ProcessServant [process_hash.Count];
				process_hash.Values.CopyTo (procs, 0);
			}

			foreach (ProcessServant proc in procs) {
				proc.Kill ();
			}

			kill_event.WaitOne ();
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

		public Process Run (DebuggerSession session)
		{
			CommandResult dummy;
			return Run (session, out dummy);
		}

		public Process Run (DebuggerSession session, out CommandResult result)
		{
			check_alive ();

			if (main_process != null)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			ProcessStart start = new ProcessStart (session);
			main_process = thread_manager.StartApplication (start, out result);
			return main_process.Client;
		}

		public Process Attach (DebuggerSession session, int pid)
		{
			CommandResult dummy;
			return Attach (session, pid, out dummy);
		}

		public Process Attach (DebuggerSession session, int pid, out CommandResult result)
		{
			check_alive ();

			if (main_process != null)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			ProcessStart start = new ProcessStart (session, pid);
			main_process = thread_manager.StartApplication (start, out result);
			return main_process.Client;
		}

		public Process OpenCoreFile (DebuggerSession session, string core_file,
					     out Thread[] threads)
		{
			check_alive ();

			if (main_process != null)
				throw new TargetException (TargetError.AlreadyHaveTarget);

			ProcessStart start = new ProcessStart (session, core_file);

			main_process = thread_manager.OpenCoreFile (start, out threads);
			process_hash.Add (main_process, main_process);
			return main_process.Client;
		}

		public bool HasTarget {
			get { return alive; }
		}

		public Process[] Processes {
			get {
				check_alive ();
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

		public bool CanDetach {
			get {
				return alive && (main_process != null) && main_process.CanDetach;
			}
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

		void OperationCompleted (SingleSteppingEngine caller, TargetEventArgs result, ThreadingModel model)
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

		void StopAll ()
		{
			foreach (ProcessServant process in process_hash.Values) {
				process.Stop ();
			}
		}

		protected class MyOperationHost : IOperationHost
		{
			public Debugger Debugger;

			public MyOperationHost (Debugger debugger)
			{
				this.Debugger = debugger;
			}

			void IOperationHost.OperationCompleted (SingleSteppingEngine caller, TargetEventArgs result, ThreadingModel model)
			{
				Debugger.OperationCompleted (caller, result, model);
			}

			WaitHandle IOperationHost.WaitHandle {
				get { return Debugger.stopped_event; }
			}

			void IOperationHost.SendResult (SingleSteppingEngine sse, TargetEventArgs args)
			{
				Debugger.OnTargetEvent (sse.Thread, args);
			}

			void IOperationHost.Abort ()
			{
				Debugger.StopAll ();
			}
		}

		protected class GlobalCommandResult : OperationCommandResult
		{
			public Debugger Debugger {
				get; private set;
			}

			internal override IOperationHost Host {
				get { return Debugger.OperationHost; }
			}

			internal GlobalCommandResult (Debugger debugger, ThreadingModel model)
				: base (model)
			{
				this.Debugger = debugger;
			}

			internal override void OnExecd (SingleSteppingEngine new_thread)
			{ }
		}

#endregion

		[Obsolete]
		GUIManager gui_manager;

		[Obsolete]
		internal GUIManager GUIManager {
			get {
				if (gui_manager == null)
					gui_manager = new GUIManager (this);

				return gui_manager;
			}
		}

		//
		// IDisposable
		//

		void check_alive ()
		{
			if (!alive)
				throw new TargetException (TargetError.NoTarget);
		}

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("DebuggerServant");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				disposed = true;
				alive = false;
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
