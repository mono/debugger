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
		Queue<Event> event_queue;
		
		object stopLock = new object ();
		List<Thread> stoppedList;

		bool stopping;
		
		BreakpointHitHandler breakpointHitHandler;

		public event TargetEventHandler TargetEvent;
		
		public BreakpointHitHandler BreakpointHitHandler {
			get { return breakpointHitHandler; }
			set { breakpointHitHandler = value; }
		}

		public void StartGUIManager ()
		{
			event_queue = new Queue<Event> ();
			manager_event = new ST.AutoResetEvent (false);
			manager_thread = new ST.Thread (new ST.ThreadStart (manager_thread_main));
			manager_thread.Start ();
		}

		internal void OnTargetEvent (SingleSteppingEngine sse, TargetEventArgs args)
		{
			switch (args.Type){
			case TargetEventType.TargetHitBreakpoint:
			case TargetEventType.Exception:
			case TargetEventType.TargetStopped:
			case TargetEventType.TargetInterrupted:
			case TargetEventType.TargetSignaled:
			case TargetEventType.UnhandledException:
				handle_autostop_event (sse, args);
				break;

			case TargetEventType.TargetExited:
			case TargetEventType.FrameChanged:
				// Ignore
				break;
			}
		}

		void handle_autostop_event (SingleSteppingEngine sse, TargetEventArgs args)
		{
			bool stopRequested = true;
			
			// Always run the breakpoint handler for breakpoint hits,
			// even when the debugger is already stopping
			if ((args.Type == TargetEventType.TargetHitBreakpoint || args.Type == TargetEventType.Exception) && breakpointHitHandler != null)
				stopRequested = breakpointHitHandler (args);
			
			lock (stopLock) {
				
				// If the debugger is already being stopped, make sure this event doesn't
				// reach the GUI.
				if (stopping) {
					if (!stoppedList.Contains (sse.Thread)) {
						stoppedList.Add (sse.Thread);
						ST.Monitor.Pulse (stopLock);
					}
					return;
				}
				
				// If the thread doesn't need to be stopped, resume it here
				if (!stopRequested) {
					Continue (sse.Thread);
					return;
				}
				
				stopping = true;
				stoppedList = new List<Thread> ();
			}

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

			QueueEvent (new StopEvent {
				Manager = this, Thread = sse.Client, Args = args,
				Stopped = stopped
			});
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

		protected void ProcessStopEvent (StopEvent e)
		{
			// Wait until all threads have reported a stop event.
			// Waiting on the wait handle is not enough since
			// events are fired asynchronously, so they might come
			// after the wait handle is signaled

			lock (stopLock) {
				while (stoppedList.Count != e.Stopped.Count) {
					ST.Monitor.Wait (stopLock);
				}
				stoppedList = null;
				stopping = false;
			}

			SendTargetEvent (e.Thread, e.Args);
		}
		
		protected void QueueEvent (Event e)
		{
			lock (event_queue) {
				event_queue.Enqueue (e);
				manager_event.Set ();
			}
		}
				
		public void Break (Thread thread)
		{
			QueueEvent (new BreakEvent { Manager = this, Thread = thread });
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

		void ProcessRunEvent (RunEvent e)
		{
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
			}
		}

		protected abstract class Event
		{
			public GUIManager Manager {
				get; set;
			}

			public abstract void ProcessEvent ();
		}

		protected class StopEvent : Event
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
				Manager.ProcessStopEvent (this);
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

		protected class BreakEvent : RunEvent
		{
			protected override void DoRun ()
			{
				Thread.ThreadServant.Stop ();
			}
		}
	}
			
	public delegate bool BreakpointHitHandler (TargetEventArgs args);
}
