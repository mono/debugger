using GLib;
using System;
using System.IO;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;

namespace Mono.Debugger.Backends
{
	public delegate bool DaemonThreadHandler (DaemonThreadRunner sender, TargetAddress address, int signal);

	// <summary>
	//   This is a minimalistic SingleSteppingEngine for undebuggable daemon threads.
	// </summary>
	public class DaemonThreadRunner : IDisposable
	{
		public DaemonThreadRunner (DebuggerBackend backend, Process process, IInferior inferior,
					   DaemonThreadHandler daemon_thread_handler, int pid, int signal)
		{
			this.backend = backend;
			this.process = process;
			this.inferior = inferior;
			this.daemon_thread_handler = daemon_thread_handler;
			this.signal = signal;
			this.pid = pid;

			thread_manager = backend.ThreadManager;

			inferior.TargetExited += new TargetExitedHandler (child_exited);

			daemon_thread = new Thread (new ThreadStart (daemon_thread_main));
			daemon_thread.Start ();
		}

		internal IInferior Inferior {
			get { return inferior; }
		}

		IInferior inferior;
		DebuggerBackend backend;
		ThreadManager thread_manager;	
		Process process;
		Thread daemon_thread;
		DaemonThreadHandler daemon_thread_handler;
		int signal;
		int pid;

		ChildEvent wait ()
		{
			ChildEvent child_event;
			do {
				child_event = inferior.Wait ();
			} while (child_event == null);
			return child_event;
		}

		protected bool daemon_stopped (TargetAddress address)
		{
			if (daemon_thread_handler == null)
				return false;
			else
				return daemon_thread_handler (this, address, 0);
		}

		protected bool daemon_received_signal (TargetAddress address, int signal)
		{
			bool action;
			if (thread_manager.SignalHandler (process, signal, out action))
				return true;
			else if (daemon_thread_handler == null)
				return false;
			else
				return daemon_thread_handler (this, address, signal);
		}

		void daemon_thread_main ()
		{
			inferior.Attach (pid);
			inferior.SetSignal (signal, false);
			inferior.Continue ();

		again:
			ChildEvent child_event = wait ();
			ChildEventType message = child_event.Type;
			int arg = child_event.Argument;

			if ((message == ChildEventType.CHILD_STOPPED) && (arg != 0)) {
				if (!daemon_received_signal (inferior.CurrentFrame, arg))
					throw new InternalError ("Daemon thread {0} received unexpected " +
								 "signal {1}.", pid, arg);
				inferior.Continue ();
				goto again;
			} else if (message == ChildEventType.CHILD_EXITED)
				return;
			else if ((message != ChildEventType.CHILD_HIT_BREAKPOINT) && (arg != 0))
				throw new InternalError ("Unexpected result from daemon thread: {0} {1}",
							 message, arg);

			if (!daemon_stopped (inferior.CurrentFrame))
				throw new InternalError ("Daemon thread unexpectedly stopped at {0}.",
							 inferior.CurrentFrame);

			inferior.Continue ();
			goto again;
		}

		void child_exited ()
		{
			inferior = null;
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("DaemonThreadRunner");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					if (daemon_thread != null) {
						daemon_thread.Abort ();
						daemon_thread = null;
					}
				}

				// Release unmanaged resources
				this.disposed = true;

				lock (this) {
					// Nothing to do yet.
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~DaemonThreadRunner ()
		{
			Dispose (false);
		}
	}
}
