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
		int frame_notify_id;
		int state_notify_id;
		int module_notify_id;
		int bpt_notify_id;

		public DebuggerManager (DebuggerGUI gui)
		{
			this.gui = gui;
			this.thread_notify = gui.ThreadNotify;

			frame_notify_id = thread_notify.RegisterListener (new ReadyEventHandler (frame_event));
			state_notify_id = thread_notify.RegisterListener (new ReadyEventHandler (state_event));
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

			if (ProcessCreatedEvent != null)
				ProcessCreatedEvent (this, process);

			backend.ModulesChangedEvent += new ModulesChangedHandler (RealModulesChanged);
			backend.BreakpointsChangedEvent += new BreakpointsChangedHandler (RealBreakpointsChanged);

			process.FrameChangedEvent += new StackFrameHandler (RealFrameChanged);
			process.FramesInvalidEvent += new StackFrameInvalidHandler (RealFramesInvalid);
			process.StateChanged += new StateChangedHandler (RealStateChanged);

			RealModulesChanged ();
			RealStateChanged (process.State, 0);
			if (process.State == TargetState.STOPPED)
				RealFrameChanged (process.CurrentFrame);
		}

		public event ProcessCreatedHandler ProcessCreatedEvent;

		StackFrame current_frame = null;
		Backtrace current_backtrace = null;

		void frame_event ()
		{
			lock (this) {
				if (current_frame != null)
					FrameChanged (current_frame);
				else
					FramesInvalid ();
			}
		}

		// <remarks>
		//   This method may get called from any thread, so we must not use gtk# here.
		// </remarks>
		protected virtual void RealFrameChanged (StackFrame frame)
		{
			lock (this) {
				current_frame = frame;
				current_backtrace = process.GetBacktrace ();
				if (RealFrameChangedEvent != null)
					RealFrameChangedEvent (frame);
				thread_notify.Signal (frame_notify_id);
			}
		}

		// <remarks>
		//   This method may get called from any thread, so we must not use gtk# here.
		// </remarks>
		protected virtual void RealFramesInvalid ()
		{
			lock (this) {
				current_frame = null;
				current_backtrace = null;
				if (RealFramesInvalidEvent != null)
					RealFramesInvalidEvent ();
				thread_notify.Signal (frame_notify_id);
			}
		}

		// <remarks>
		//   These two events may be emitted from any thread, so we must not use gtk# here.
		// </remarks>
		public event StackFrameHandler RealFrameChangedEvent;
		public event StackFrameInvalidHandler RealFramesInvalidEvent;

		// <remarks>
		//   This method will always get called from the gtk# thread and while keeping the
		//   `this' lock.
		// </remarks>
		protected virtual void FrameChanged (StackFrame frame)
		{
			if (FrameChangedEvent != null)
				FrameChangedEvent (frame);
		}

		// <remarks>
		//   This method will always get called from the gtk# thread and while keeping the
		//   `this' lock.
		// </remarks>
		protected virtual void FramesInvalid ()
		{
			if (FramesInvalidEvent != null)
				FramesInvalidEvent ();
		}

		public event StackFrameHandler FrameChangedEvent;
		public event StackFrameInvalidHandler FramesInvalidEvent;

		public StackFrame CurrentFrame {
			get { return current_frame; }
		}

		public Backtrace CurrentBacktrace {
			get { return current_backtrace; }
		}

		TargetState current_state = TargetState.NO_TARGET;
		int current_state_arg = 0;
		bool state_changed = false;

		void state_event ()
		{
			lock (this) {
				if (state_changed) {
					state_changed = false;
					StateChanged (current_state, current_state_arg);
				}
			}
		}

		// <remarks>
		//   This method may get called from any thread, so we must not use gtk# here.
		// </remarks>
		protected virtual void RealStateChanged (TargetState state, int arg)
		{
			lock (this) {
				state_changed = true;
				current_state = state;
				current_state_arg = arg;

				thread_notify.Signal (state_notify_id);
			}
		}

		// <remarks>
		//   This method will always get called from the gtk# thread and while keeping the
		//   `this' lock.
		// </remarks>
		protected virtual void StateChanged (TargetState state, int arg)
		{
			if (StateChangedEvent != null)
				StateChangedEvent (state, arg);
			if (state == TargetState.EXITED)
				process = null;
		}

		public event StateChangedHandler StateChangedEvent;

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
