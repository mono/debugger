using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Diagnostics;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Architecture;

namespace Mono.Debugger.Backends
{
	internal class PTraceInferior : Inferior
	{
		[DllImport("monodebuggerserver")]
		static extern ChildEventType mono_debugger_server_dispatch_event (IntPtr handle, int status, out long arg, out long data1, out long data2);

		[DllImport("monodebuggerserver")]
		static extern TargetError mono_debugger_server_get_signal_info (IntPtr handle, ref SignalInfo sinfo);

		private struct SignalInfo
		{
			public int SIGKILL;
			public int SIGSTOP;
			public int SIGINT;
			public int SIGCHLD;

			public int MonoThreadAbortSignal;

			public override string ToString ()
			{
				return String.Format ("SignalInfo ({0}:{1}:{2}:{3} - {4})",
						      SIGKILL, SIGSTOP, SIGINT, SIGCHLD,
						      MonoThreadAbortSignal);
			}
		}

		bool has_signals;
		SignalInfo signal_info;

		public PTraceInferior (DebuggerBackend backend, ProcessStart start,
				       BreakpointManager bpm,
				       DebuggerErrorHandler error_handler,
				       AddressDomain global_domain,
				       ThreadManager thread_manager)
			: base (backend, start, bpm, error_handler, global_domain)
		{
			this.thread_manager = thread_manager;
		}

		public override Inferior CreateThread ()
		{
			return new PTraceInferior (backend, start, breakpoint_manager,
						   error_handler, global_address_domain,
						   thread_manager);
		}

		protected override void SetupInferior ()
		{
			check_error (mono_debugger_server_get_signal_info (server_handle, ref signal_info));
			has_signals = true;

			base.SetupInferior ();
		}

		public override ChildEvent ProcessEvent (int status)
		{
			long arg, data1, data2;
			ChildEventType message;

			message = mono_debugger_server_dispatch_event (
				server_handle, status, out arg, out data1, out data2);

			switch (message) {
			case ChildEventType.CHILD_EXITED:
			case ChildEventType.CHILD_SIGNALED:
				change_target_state (TargetState.EXITED);
				break;

			case ChildEventType.CHILD_CALLBACK:
			case ChildEventType.CHILD_STOPPED:
			case ChildEventType.CHILD_HIT_BREAKPOINT:
				change_target_state (TargetState.STOPPED);
				break;
			}

			return new ChildEvent (message, arg, data1, data2);
		}

		public override int SIGKILL {
			get {
				if (!has_signals || (signal_info.SIGKILL < 0))
					throw new InvalidOperationException ();

				return signal_info.SIGKILL;
			}
		}

		public override int SIGSTOP {
			get {
				if (!has_signals || (signal_info.SIGSTOP < 0))
					throw new InvalidOperationException ();

				return signal_info.SIGSTOP;
			}
		}

		public override int SIGINT {
			get {
				if (!has_signals || (signal_info.SIGINT < 0))
					throw new InvalidOperationException ();

				return signal_info.SIGINT;
			}
		}

		public override int SIGCHLD {
			get {
				if (!has_signals || (signal_info.SIGCHLD < 0))
					throw new InvalidOperationException ();

				return signal_info.SIGCHLD;
			}
		}

		public override int MonoThreadAbortSignal {
			get {
				if (!has_signals || (signal_info.MonoThreadAbortSignal < 0))
					throw new InvalidOperationException ();

				return signal_info.MonoThreadAbortSignal;
			}
		}

		~PTraceInferior ()
		{
			Dispose (false);
		}
	}
}
