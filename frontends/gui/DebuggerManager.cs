using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	// <summary>
	//   This is used to work around the non-thread-safe gtk+.
	//   It queues all the events which may be emitted from any thread and then sends them
	//   from the gtk+ thread.
	// </summary>
	public class DebuggerManager
	{
		DebuggerBackend backend;
		Process process;
		DebuggerGUI gui;
		ThreadNotify thread_notify;
		int target_notify_id;
		int module_notify_id;
		int bpt_notify_id;

		public DebuggerManager (DebuggerGUI gui)
		{
			this.gui = gui;
			this.thread_notify = gui.ThreadNotify;

			target_notify_id = thread_notify.RegisterListener (new ReadyEventHandler (target_event));
			module_notify_id = thread_notify.RegisterListener (new ReadyEventHandler (module_event));
			bpt_notify_id = thread_notify.RegisterListener (new ReadyEventHandler (bpt_event));
		}

		public DebuggerBackend DebuggerBackend {
			get { return backend; }
		}

		public Process Process {
			get { return process; }
		}

		internal void MainProcessCreated (Process process)
		{
			this.backend = process.DebuggerBackend;
			this.process = process;
			this.target_exited = false;

			if (ProcessCreatedEvent != null)
				ProcessCreatedEvent (this, process);

			backend.ModulesChangedEvent += new ModulesChangedHandler (RealModulesChanged);
			backend.BreakpointsChangedEvent += new BreakpointsChangedHandler (RealBreakpointsChanged);

			process.TargetEvent += new TargetEventHandler (real_target_event);

			RealModulesChanged ();
		}

		internal void TargetExited ()
		{
			if (!target_exited)
				OnRealTargetEvent (new TargetEventArgs (TargetEventType.TargetExited, 0));
		}

		public event ProcessCreatedHandler ProcessCreatedEvent;

		public StackFrame CurrentFrame {
			get { return current_frame; }
		}

		public Backtrace CurrentBacktrace {
			get { return current_backtrace; }
		}

		bool target_exited = false;
		StackFrame current_frame = null;
		Backtrace current_backtrace = null;
		TargetEventArgs current_event = null;

		void real_target_event (object sender, TargetEventArgs args)
		{
			OnRealTargetEvent (args);
		}

		void target_event ()
		{
			lock (this) {
				if (current_event != null)
					OnTargetEvent (current_event);
				current_event = null;
			}
		}

		// <remarks>
		//   This method may get called from any thread, so we must not use gtk# here.
		// </remarks>
		protected virtual void OnRealTargetEvent (TargetEventArgs args)
		{
			lock (this) {
				if (args.IsStopped) {
					current_frame = args.Frame;
					current_backtrace = process.GetBacktrace ();
				} else {
					current_frame = null;
					current_backtrace = null;
				}

				if ((args.Type == TargetEventType.TargetExited) ||
				    (args.Type == TargetEventType.TargetSignaled))
					target_exited = true;

				current_event = args;
				thread_notify.Signal (target_notify_id);
			}
		}

		// <remarks>
		//   This method will always get called from the gtk# thread and while keeping the
		//   `this' lock.
		// </remarks>
		protected virtual void OnTargetEvent (TargetEventArgs args)
		{
			if (TargetEvent != null)
				TargetEvent (this, args);
		}

		public event TargetEventHandler TargetEvent;

		void module_event ()
		{
			lock (this) {
				ModulesChanged ();
			}
		}

		// <remarks>
		//   This method may get called from any thread, so we must not use gtk# here.
		// </remarks>
		protected virtual void RealModulesChanged ()
		{
			lock (this) {
				thread_notify.Signal (module_notify_id);
			}
		}

		// <remarks>
		//   This method will always get called from the gtk# thread and while keeping the
		//   `this' lock.
		// </remarks>
		protected virtual void ModulesChanged ()
		{
			if (ModulesChangedEvent != null)
				ModulesChangedEvent ();
		}

		public event ModulesChangedHandler ModulesChangedEvent;

		void bpt_event ()
		{
			lock (this) {
				BreakpointsChanged ();
			}
		}

		// <remarks>
		//   This method may get called from any thread, so we must not use gtk# here.
		// </remarks>
		protected virtual void RealBreakpointsChanged ()
		{
			lock (this) {
				thread_notify.Signal (bpt_notify_id);
			}
		}

		// <remarks>
		//   This method will always get called from the gtk# thread and while keeping the
		//   `this' lock.
		// </remarks>
		protected virtual void BreakpointsChanged ()
		{
			if (BreakpointsChangedEvent != null)
				BreakpointsChangedEvent ();
		}

		public event BreakpointsChangedHandler BreakpointsChangedEvent;
	}
}
