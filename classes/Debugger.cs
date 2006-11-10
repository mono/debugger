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

	public class Debugger : DebuggerMarshalByRefObject
	{
#if USE_DOMAIN
		AppDomain domain;
#endif
		DebuggerServant servant;
		ManualResetEvent kill_event;

		public Debugger (DebuggerConfiguration config)
		{
			kill_event = new ManualResetEvent (false);

#if USE_DOMAIN
			domain = AppDomain.CreateDomain ("mdb");

			ObjectHandle oh = domain.CreateInstance (
				Assembly.GetExecutingAssembly ().FullName,
				typeof (DebuggerServant).FullName, false,
				BindingFlags.Instance | BindingFlags.NonPublic,
				null, new object [] { this, Report.ReportWriter, config },
				null, null, null);

			servant = (DebuggerServant) oh.Unwrap ();
#else
			servant = new DebuggerServant (this, Report.ReportWriter, config);
#endif
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

		internal Process CreateProcess (ProcessServant servant)
		{
			return new Process (this, servant);
		}

		internal Thread CreateThread (ThreadServant servant, int id)
		{
			return new Thread (servant, id);
		}

		internal void OnProcessCreatedEvent (Process process)
		{
			if (ProcessCreatedEvent != null)
				ProcessCreatedEvent (this, process);
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

		internal void OnProcessExitedEvent (Process process)
		{
			if (ProcessExitedEvent != null)
				ProcessExitedEvent (this, process);
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

		internal void OnTargetEvent (Thread thread, TargetEventArgs args)
		{
			if (TargetEvent != null)
				TargetEvent (thread, args);
		}

		public void Kill ()
		{
			if (servant != null) {
				servant.Kill ();
				kill_event.WaitOne ();
			}
		}

		public void Detach ()
		{
			check_servant ();
			servant.Detach ();
		}

		public Process Run (DebuggerSession session)
		{
			check_servant ();
			return servant.Run (session);
		}

		public Process Attach (DebuggerSession session, int pid)
		{
			check_servant ();
			return servant.Attach (session, pid);
		}

		public Process OpenCoreFile (DebuggerSession session, string core_file,
					     out Thread[] threads)
		{
			check_servant ();
			return servant.OpenCoreFile (session, core_file, out threads);
		}


		public Process[] Processes {
			get {
				check_servant ();
				return servant.Processes;
			}
		}

		//
		// IDisposable
		//

		void check_servant ()
		{
			if (servant == null)
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
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				if (servant != null) {
					servant.Dispose ();
					servant = null;
				}

#if USE_DOMAIN
				if (domain != null) {
					AppDomain.Unload (domain);
					domain = null;
				}
#endif
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
