using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using ST = System.Threading;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using Mono.Debugger.Backend;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger
{
	public delegate bool ExceptionCatchPointHandler (string exception, out ExceptionAction action);

	[Serializable]
	public enum ExceptionAction
	{
		None = 0,
		Stop = 1,
		StopUnhandled = 2
	}

	public class Process : DebuggerMarshalByRefObject
	{
		Debugger debugger;
		ProcessServant servant;

		int id = ++next_id;
		static int next_id = 0;

		internal Process (Debugger debugger, ProcessServant servant)
		{
			this.debugger = debugger;
			this.servant = servant;
		}

		public int ID {
			get { return id; }
		}

		public Debugger Debugger {
			get { return debugger; }
		}

		public DebuggerSession Session {
			get { return servant.Session; }
		}

		internal ProcessServant Servant {
			get { return servant; }
		}

		internal ProcessStart ProcessStart {
			get { return servant.ProcessStart; }
		}

		public Thread MainThread {
			get { return servant.MainThread.Client; }
		}

		public bool IsManaged {
			get { return servant.IsManaged; }
		}

		public string TargetApplication {
			get { return servant.TargetApplication; }
		}

		public string[] CommandLineArguments {
			get { return servant.CommandLineArguments; }
		}

		public Language NativeLanguage {
			get { return servant.NativeLanguage; }
		}

		public ST.WaitHandle WaitHandle {
			get { return servant.WaitHandle; }
		}

		public void Kill ()
		{
			servant.Kill ();
		}

		public void Detach ()
		{
			servant.Detach ();
		}

		public void LoadLibrary (Thread thread, string filename)
		{
			servant.LoadLibrary (thread, filename);
		}

		public Module[] Modules {
			get { return servant.Modules; }
		}

		public Method Lookup (TargetAddress address)
		{
			return servant.SymbolTableManager.Lookup (address);
		}

		public TargetAddress LookupSymbol (string name)
		{
			return servant.LookupSymbol (name);
		}

		public Thread[] GetThreads ()
		{
			return servant.GetThreads ();
		}

		public override string ToString ()
		{
			return String.Format ("Process #{0}", ID);
		}

		public event TargetOutputHandler TargetOutputEvent;

		internal void OnTargetOutput (bool is_stderr, string output)
		{
			if (TargetOutputEvent != null)
				TargetOutputEvent (is_stderr, output);
		}

		//
		// Test
		//

		public CommandResult ActivatePendingBreakpoints ()
		{
			if (!Session.HasPendingBreakpoints ())
				return null;

			ProcessCommandResult result = new ProcessCommandResult (this);
			servant.ActivatePendingBreakpoints (result);
			return result;
		}

		protected class ProcessCommandResult : CommandResult
		{
			Process process;
			ST.ManualResetEvent completed_event = new ST.ManualResetEvent (false);

			internal ProcessCommandResult (Process process)
			{
				this.process = process;
			}

			public Process Process {
				get { return process; }
			}

			public override ST.WaitHandle CompletedEvent {
				get { return completed_event; }
			}

			internal override void Completed ()
			{
				completed_event.Set ();
			}

			public override void Abort ()
			{
				throw new NotImplementedException ();
			}
		}

		//
		// Stopping / resuming all threads for the GUI
		//

		[Obsolete]
		public GUIManager StartGUIManager ()
		{
			Session.Config.StopOnManagedSignals = false;
			return Debugger.GUIManager;
		}

		internal void OnTargetEvent (SingleSteppingEngine sse, TargetEventArgs args)
		{
			Debugger.OnTargetEvent (sse.Client, args);
		}

		internal void OnEnterNestedBreakState (SingleSteppingEngine sse)
		{
			Debugger.OnEnterNestedBreakState (sse.Client);
		}

		internal void OnLeaveNestedBreakState (SingleSteppingEngine sse)
		{
			Debugger.OnLeaveNestedBreakState (sse.Client);
		}

		ExceptionCatchPointHandler generic_exc_handler;

		public void InstallGenericExceptionCatchPoint (ExceptionCatchPointHandler handler)
		{
			this.generic_exc_handler = handler;
		}

		public bool GenericExceptionCatchPoint (string exception, out ExceptionAction action)
		{
			if (generic_exc_handler != null)
				return generic_exc_handler (exception, out action);

			action = ExceptionAction.None;
			return false;
		}

		[Obsolete("Use DebuggerSession.InsertExceptionCatchPoint().")]
		public int AddExceptionCatchPoint (ExceptionCatchPoint catchpoint)
		{
			check_disposed ();
			// Do nothing; the session now maintains exception catchpoints.
			return catchpoint.UniqueID;
		}

		[Obsolete("Use DebuggerSession.RemoveEvent().")]
		public void RemoveExceptionCatchPoint (int index)
		{
			check_disposed ();
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Process");
		}

		private void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				if (servant != null) {
					servant.Dispose ();
					servant = null;
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Process ()
		{
			Dispose (false);
		}
	}
}
