using System;
using System.Linq;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using ST = System.Threading;

using Mono.Debugger.Backend;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger
{
	using EE = ExpressionEvaluator;

	public class GUIManager : DebuggerMarshalByRefObject
	{
		public Process Process {
			get; private set;
		}

		internal GUIManager (Process process)
		{
			this.Process = process;
		}

		ST.Thread manager_thread;
		ST.AutoResetEvent manager_event;
		ST.ManualResetEvent result_event;
		bool want_result;
		Queue<Event> event_queue;

		bool break_mode;
		bool suppress_events;
		long current_evaluation;

		public event TargetEventHandler TargetEvent;
		public event ProcessEventHandler ProcessExitedEvent;

		public void StartGUIManager ()
		{
			Process.Session.Config.StopOnManagedSignals = false;

			event_queue = new Queue<Event> ();
			manager_event = new ST.AutoResetEvent (false);
			result_event = new ST.ManualResetEvent (false);
			manager_thread = new ST.Thread (new ST.ThreadStart (manager_thread_main));
			manager_thread.Start ();
		}

		internal void OnProcessExited (Process process)
		{
			try {
				if (ProcessExitedEvent != null)
					ProcessExitedEvent (process.Debugger, process);
			} catch (Exception ex) {
				Report.Error ("Caught exception while sending process {0} exit:\n{1}",
					       process, ex);
			}
		}

		internal void OnTargetEvent (SingleSteppingEngine sse, TargetEventArgs args)
		{
			switch (args.Type){
			case TargetEventType.TargetHitBreakpoint:
			case TargetEventType.TargetStopped:
			case TargetEventType.UnhandledException:
			case TargetEventType.Exception:
				if (!suppress_events)
					handle_autostop_event (sse, args);
				break;

			case TargetEventType.TargetInterrupted:
				if (!break_mode && !suppress_events) {
					args = new TargetEventArgs (TargetEventType.TargetStopped, 0);
					handle_autostop_event (sse, args);
				}
				break;

			case TargetEventType.RuntimeInvokeDone:
				if (!suppress_events)
					handle_autostop_event (sse, args);
				break;

			default:
				SendTargetEvent (sse.Thread, args);
				break;
			}
		}

		void handle_autostop_event (SingleSteppingEngine sse, TargetEventArgs args)
		{
			break_mode = true;

			List<Thread> stopped = new List<Thread> ();

			Thread[] threads = Process.GetThreads ();
			foreach (Thread thread in threads) {
				if (thread == sse.Client)
					continue;

				// Never touch immutable threads.
				if ((thread.ThreadFlags & Thread.Flags.Immutable) != 0)
					continue;
				// Background thread -> keep running.
				if ((thread.ThreadFlags & Thread.Flags.Background) != 0)
					continue;

				Report.Debug (DebugFlags.Threads, "Autostopping thread: {0}", thread);
				thread.Stop ();
				thread.SetThreadFlags (thread.ThreadFlags | Thread.Flags.AutoRun);
				stopped.Add (thread);
			}

			lock (event_queue) {
				event_queue.Enqueue (new StoppingEvent {
					Manager = this, Thread = sse.Client, Args = args,
					Stopped = stopped
				});
				manager_event.Set ();
			}
		}

		internal void SendTargetEvent (Thread thread, TargetEventArgs args)
		{
			try {
				if (TargetEvent != null)
					TargetEvent (thread, args);
			} catch (Exception ex) {
				Report.Error ("{0} caught exception while sending {1}:\n{2}",
					      thread, args, ex);
			}
		}

		protected void ProcessStoppingEvent (StoppingEvent e)
		{
			List<ST.WaitHandle> wait_list = new List<ST.WaitHandle> ();
			foreach (Thread thread in e.Stopped)
				wait_list.Add (thread.WaitHandle);

			ST.WaitHandle.WaitAll (wait_list.ToArray ());

			SendTargetEvent (e.Thread, e.Args);
		}

		protected object QueueEvent (Event e)
		{
			lock (event_queue) {
				event_queue.Enqueue (e);
				manager_event.Set ();
				result_event.Reset ();
				want_result = true;
			}
			result_event.WaitOne ();
			if (e.Result is Exception)
				throw (Exception) e.Result;
			return e.Result;
		}

		public void Stop (Thread thread)
		{
			QueueEvent (new StopEvent { Manager = this, Thread = thread });
		}

		public void Continue (Thread thread)
		{
			QueueEvent (new ContinueEvent { Manager = this, Thread = thread });
		}

		public void StepInto (Thread thread)
		{
			QueueEvent (new StepIntoEvent { Manager = this, Thread = thread });
		}

		public void StepOver (Thread thread)
		{
			QueueEvent (new StepOverEvent { Manager = this, Thread = thread });
		}

		public void StepOut (Thread thread)
		{
			QueueEvent (new StepOutEvent { Manager = this, Thread = thread });
		}

		public EE.EvaluationResult MonoObjectToString (Thread thread, TargetStructObject obj,
							       EE.EvaluationFlags flags, int timeout,
							       out string text)
		{
			if (!break_mode)
				throw new InvalidOperationException ();

			EE.EvaluationResult result;

			try {
				suppress_events = true;
				result = EE.MonoObjectToString (thread, obj, flags, timeout, out text);
			} finally {
				suppress_events = false;
			}

			return result;
		}

		public EE.EvaluationResult GetProperty (Thread thread, TargetPropertyInfo property,
							TargetStructObject instance, EE.EvaluationFlags flags,
							int timeout, out string error, out TargetObject value)
		{
			if (!break_mode)
				throw new InvalidOperationException ();

			EE.EvaluationResult result;

			try {
				suppress_events = true;
				result = EE.GetProperty (thread, property, instance, flags, timeout,
							 out error, out value);
			} finally {
				suppress_events = false;
			}

			return result;
		}

		public EE.AsyncResult EvaluateExpressionAsync (StackFrame frame, EE.IExpression expression,
							       EE.EvaluationFlags flags, EE.EvaluationCallback cb)
		{
			if (!break_mode)
				throw new InvalidOperationException ();

			return (EE.AsyncResult) QueueEvent (new EvaluateAsyncEvent {
				Manager = this, Frame = frame, Expression = expression,
				Flags = flags, Callback = cb
			});
		}

		void ProcessRunEvent (RunEvent e)
		{
			break_mode = false;
			Thread[] threads = Process.GetThreads ();
			foreach (Thread t in threads) {
				if (t == e.Thread)
					continue;

				TargetState state = t.ThreadServant.State;
				if (state != TargetState.Stopped)
					continue;

				if ((t.ThreadFlags & Thread.Flags.AutoRun) != 0)
					t.Continue ();
			}
		}

		void manager_thread_main ()
		{
			while (true) {
				manager_event.WaitOne ();

				Event e;
				lock (event_queue) {
					e = event_queue.Dequeue ();
				}
				e.ProcessEvent ();
				lock (event_queue) {
					if (want_result)
						result_event.Set ();
					want_result = false;
				}
			}
		}

		protected abstract class Event
		{
			public GUIManager Manager {
				get; set;
			}

			public object Result {
				get; protected set;
			}

			public abstract void ProcessEvent ();
		}

		protected class EvaluateAsyncEvent : Event
		{
			public StackFrame Frame {
				get; set;
			}

			public EE.IExpression Expression {
				get; set;
			}

			public EE.EvaluationFlags Flags {
				get; set;
			}

			public EE.EvaluationCallback Callback {
				get; set;
			}

			public override void ProcessEvent ()
			{
				Manager.break_mode = false;
				Result = Expression.Evaluate (Frame, Flags, EvaluationDone);
			}

			void EvaluationDone (EE.EvaluationResult result, object data)
			{
				Manager.break_mode = true;
				Callback (result, data);
			}
		}

		protected class StoppingEvent : Event
		{
			public Thread Thread {
				get; set;
			}

			public TargetEventArgs Args {
				get; set;
			}

			public List<Thread> Stopped {
				get; set;
			}

			public override void ProcessEvent ()
			{
				Manager.ProcessStoppingEvent (this);
			}
		}


		protected abstract class RunEvent : Event
		{
			public Thread Thread {
				get; set;
			}

			public override void ProcessEvent ()
			{
				Manager.ProcessRunEvent (this);
				DoRun ();
			}

			protected abstract void DoRun ();
		}

		protected class ContinueEvent : RunEvent
		{
			protected override void DoRun ()
			{
				Thread.ThreadServant.Continue (TargetAddress.Null, null);
			}
		}

		protected class StepIntoEvent : RunEvent
		{
			protected override void DoRun ()
			{
				Thread.ThreadServant.StepLine (null);
			}
		}

		protected class StepOverEvent : RunEvent
		{
			protected override void DoRun ()
			{
				Thread.ThreadServant.NextLine (null);
			}
		}

		protected class StepOutEvent : RunEvent
		{
			protected override void DoRun ()
			{
				Thread.ThreadServant.Finish (false, null);
			}
		}

		protected class StopEvent : Event
		{
			public Thread Thread {
				get; set;
			}

			public override void ProcessEvent ()
			{
				Thread.ThreadServant.Stop ();
			}
		}
	}
}
