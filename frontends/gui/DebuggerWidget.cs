using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public abstract class DebuggerWidget
	{
		protected Gtk.Widget container;
		protected Gtk.Widget widget;
		protected DebuggerBackend backend;
		protected Process process;
		protected DebuggerGUI gui;
		protected ThreadNotify thread_notify;
		protected Glade.XML gxml;
		bool visible;
		int frame_notify_id;
		int state_notify_id;
		int method_notify_id;

		public DebuggerWidget (DebuggerGUI gui, Gtk.Container container, Gtk.Widget widget)
		{
			this.gui = gui;
			this.thread_notify = gui.ThreadNotify;
			this.gxml = gui.GXML;
			this.widget = widget;
			this.container = container;

			frame_notify_id = thread_notify.RegisterListener (new ReadyEventHandler (frame_event));
			state_notify_id = thread_notify.RegisterListener (new ReadyEventHandler (state_event));
			method_notify_id = thread_notify.RegisterListener (new ReadyEventHandler (method_event));

			if (container == null) {
				Gtk.Widget parent = widget.Parent;
				while (!(parent is Gtk.Window))
					parent = parent.Parent;

				container = (Gtk.Container) parent;
			}

			visible = container.Visible;

			try {
				container.Mapped += new EventHandler (mapped);
				container.Unmapped += new EventHandler (unmapped);
			} catch {}
		}

		public virtual void SetBackend (DebuggerBackend backend, Process process)
		{
			this.backend = backend;
			this.process = process;

			process.FrameChangedEvent += new StackFrameHandler (RealFrameChanged);
			process.FramesInvalidEvent += new StackFrameInvalidHandler (RealFramesInvalid);
			process.StateChanged += new StateChangedHandler (RealStateChanged);
			process.MethodChangedEvent += new MethodChangedHandler (RealMethodChanged);
			process.MethodInvalidEvent += new MethodInvalidHandler (RealMethodInvalid);
		}

		StackFrame current_frame = null;

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
				thread_notify.Signal (frame_notify_id);
			}
		}

		// <remarks>
		//   This method will always get called from the gtk# thread and while keeping the
		//   `this' lock.
		// </remarks>
		protected virtual void FrameChanged (StackFrame frame)
		{ }

		// <remarks>
		//   This method will always get called from the gtk# thread and while keeping the
		//   `this' lock.
		// </remarks>
		protected virtual void FramesInvalid ()
		{ }

		protected StackFrame CurrentFrame {
			get { return current_frame; }
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
		{ }

		IMethod current_method = null;
		IMethodSource current_method_source = null;

		void method_event ()
		{
			lock (this) {
				if (current_method != null)
					MethodChanged (current_method, current_method_source);
				else
					MethodInvalid ();
			}
		}

		// <remarks>
		//   This method may get called from any thread, so we must not use gtk# here.
		// </remarks>
		protected virtual void RealMethodChanged (IMethod method)
		{
			lock (this) {
				current_method = method;
				if ((method != null) && method.HasSource)
					current_method_source = method.Source;
				else
					current_method_source = null;
				thread_notify.Signal (method_notify_id);
			}
		}

		// <remarks>
		//   This method may get called from any thread, so we must not use gtk# here.
		// </remarks>
		protected virtual void RealMethodInvalid ()
		{
			lock (this) {
				current_method = null;
				current_method_source = null;
				thread_notify.Signal (method_notify_id);
			}
		}

		// <remarks>
		//   This method will always get called from the gtk# thread and while keeping the
		//   `this' lock.
		// </remarks>
		protected virtual void MethodChanged (IMethod method, IMethodSource source)
		{ }

		// <remarks>
		//   This method will always get called from the gtk# thread and while keeping the
		//   `this' lock.
		// </remarks>
		protected virtual void MethodInvalid ()
		{ }

		protected IMethod CurrentMethod {
			get { return current_method; }
		}
		
		void mapped (object o, EventArgs args)
		{
			visible = true;
		}

		void unmapped (object o, EventArgs args)
		{
			visible = false;
		}

		public DebuggerWidget (DebuggerGUI gui, Gtk.Widget widget)
			: this (gui, null, widget)
		{ }

		public virtual Gtk.Widget Widget {
			get {
				return widget;
			}
		}

		public virtual Gtk.Widget Container {
			get {
				return container;
			}
		}

		public virtual DebuggerBackend Debugger {
			get {
				return backend;
			}
		}

		public virtual void Show ()
		{
			container.Show ();
			visible = true;
		}

		public virtual void Hide ()
		{
			container.Hide ();
			visible = false;
		}

		public virtual bool IsVisible {
			get {
				return visible;
			}
		}
	}
}
