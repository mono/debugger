using System;
using System.IO;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;

namespace Mono.Debugger.Backends
{
	internal delegate bool DaemonThreadHandler (DaemonThreadRunner sender, TargetAddress address, int signal);

	// <summary>
	//   This is a minimalistic SingleSteppingEngine for undebuggable daemon threads.
	// </summary>
	internal class DaemonThreadRunner : IDisposable
	{
		public DaemonThreadRunner (Process process, Inferior inferior, int pid,
					   SingleSteppingEngine sse, DaemonThreadHandler handler)
		{
			this.process = process;
			this.inferior = inferior;
			this.pid = pid;
			this.daemon_thread_handler = handler;

			process.DaemonEvent += new DaemonEventHandler (daemon_event);
			process.TargetExitedEvent += new TargetExitedHandler (target_exited);
		}

		public void Run ()
		{
			// process.Run ();
			process.Continue (true, false);
		}

		void target_exited ()
		{
			if (TargetExited != null)
				TargetExited ();
		}

		bool daemon_event (TargetEventArgs args)
		{
			if ((args.Type == TargetEventType.TargetStopped) && ((int) args.Data != 0)) {
				int signal = (int) args.Data;

				if (signal == inferior.SIGKILL) {
					Console.WriteLine ("Daemon thread {0} received SIGKILL.", pid);
					return false;
				}
				if (!daemon_received_signal (inferior.CurrentFrame, signal))
					Console.WriteLine ("Daemon thread {0} received unexpected " +
							   "signal {1} at {2}.", pid, signal,
							   Inferior.CurrentFrame);
			} else if (args.Type == TargetEventType.TargetExited) {
				return false;
			} else if (args.Type == TargetEventType.TargetSignaled) {
				int signal = (int) args.Data;

				if (signal == inferior.SIGKILL)
					return false;
				else
					Console.WriteLine ("Daemon thread {0} unexpectedly died with " +
							   "fatal signal {1}.", pid, signal);
			} else if ((args.Type == TargetEventType.TargetHitBreakpoint) && (args.Data == null))
				return true;
			else if (!daemon_stopped (inferior.CurrentFrame))
				Console.WriteLine ("Daemon thread {0} stopped unexpectedly at {1}.", pid, inferior.CurrentFrame);
			else
				return true;

#if FIXME
			if (is_main_thread)
				backend.ThreadManager.StartApplicationError ();
#endif

			return false;
		}

		public Process Process {
			get { return process; }
		}

		internal Inferior Inferior {
			get { return inferior; }
		}

		public event TargetExitedHandler TargetExited;

		Inferior inferior;
		Process process;
		DaemonThreadHandler daemon_thread_handler;
		ProcessStart start;
		bool is_main_thread;
		bool redirect_fds;
		int pid;

		protected bool daemon_stopped (TargetAddress address)
		{
			if (daemon_thread_handler == null)
				return false;
			else
				return daemon_thread_handler (this, address, 0);
		}

		protected bool daemon_received_signal (TargetAddress address, int signal)
		{
			if (daemon_thread_handler == null)
				return false;
			else
				return daemon_thread_handler (this, address, signal);
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
			if (disposed)
				return;

			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				if (process != null) {
					process.Kill ();
					process = null;
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
