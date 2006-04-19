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

	public class Debugger : MarshalByRefObject
	{
		DebuggerServant servant;

		public Debugger ()
		{
			this.servant = new DebuggerServant (this);
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
			if (ProcessCreatedEvent != null)
				ProcessCreatedEvent (this, process);
		}

		internal void OnTargetExitedEvent ()
		{
			if (TargetExitedEvent != null)
				TargetExitedEvent (this);
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
			servant.Kill ();
		}

		public void Detach ()
		{
			servant.Detach ();
		}

		public Process Run (DebuggerOptions options)
		{
			return servant.Run (options);
		}

		public Process Attach (DebuggerOptions options, int pid)
		{
			return servant.Attach (options, pid);
		}

		public Process OpenCoreFile (DebuggerOptions options, string core_file,
					     out Thread[] threads)
		{
			return servant.OpenCoreFile (options, core_file, out threads);
		}


		public Process[] Processes {
			get { return servant.Processes; }
		}

		//
		// IDisposable
		//

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
