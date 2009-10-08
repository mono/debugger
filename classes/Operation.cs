using System;
using ST = System.Threading;

using Mono.Debugger.Backend;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	[Flags]
	public enum ThreadingModel
	{
		Default			= 0,
		Single			= 1,
		Process			= 2,
		Global			= 3,

		ResumeThreads		= 0x0100,

		StopDaemonThreads	= 0x0200,
		StopImmutableThreads	= 0x0400,

		ThreadingMode		= 0x00FF,
		ThreadingFlags		= 0xFF00
	}

	public abstract class CommandResult : DebuggerMarshalByRefObject
	{
		public object Result;

		public abstract ST.WaitHandle CompletedEvent {
			get;
		}

		internal abstract void Completed ();

		public abstract void Abort ();

		public void Wait ()
		{
			CompletedEvent.WaitOne ();
			if (Result is Exception)
				throw (Exception) Result;
		}
	}

	internal interface IOperationHost
	{
		ST.WaitHandle WaitHandle {
			get;
		}

		void OperationCompleted (SingleSteppingEngine sse, TargetEventArgs args, ThreadingModel model);

		void SendResult (SingleSteppingEngine sse, TargetEventArgs args);

		void Abort ();
	}

	public abstract class OperationCommandResult : CommandResult
	{
		public ThreadingModel ThreadingModel {
			get; private set;
		}

		internal abstract IOperationHost Host {
			get;
		}

		public bool IsCompleted {
			get; private set;
		}

		internal OperationCommandResult (ThreadingModel model)
		{
			this.ThreadingModel = model;
		}

		public override ST.WaitHandle CompletedEvent {
			get { return Host.WaitHandle; }
		}

		internal override void Completed ()
		{ }

		internal virtual void Completed (SingleSteppingEngine sse, TargetEventArgs args)
		{
			if ((args != null) && ((args.Type == TargetEventType.TargetExited) || (args.Type == TargetEventType.TargetSignaled))) {
				if ((sse.Thread.ThreadFlags & Thread.Flags.StopOnExit) == 0) {
					Host.SendResult (sse, args);
					return;
				}
			}

			if (!IsCompleted) {
				IsCompleted = true;
				Host.OperationCompleted (sse, args, ThreadingModel);
				if (args != null)
					Host.SendResult (sse, args);
			}
		}

		internal abstract void OnExecd (SingleSteppingEngine new_thread);

		public override void Abort ()
		{
			Host.Abort ();
		}
	}
}
