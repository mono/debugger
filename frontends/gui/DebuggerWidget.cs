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
		protected Glade.XML gxml;
		protected DebuggerManager manager;
		bool visible;

		public DebuggerWidget (DebuggerGUI gui, Gtk.Container container, Gtk.Widget widget)
		{
			this.gui = gui;
			this.manager = gui.Manager;
			this.gxml = gui.GXML;
			this.widget = widget;
			this.container = container;

			gui.Manager.ProcessCreatedEvent += new ProcessCreatedHandler (ProgramLoaded);

			gui.Manager.TargetEvent += new TargetEventHandler (target_event);

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

		public DebuggerBackend DebuggerBackend {
			get { return backend; }
		}

		public Process Process {
			get { return process; }
		}

		void ProgramLoaded (object sender, Process process)
		{
			this.backend = process.DebuggerBackend;
			SetProcess (process);
		}

		protected virtual void SetProcess (Process process)
		{
			this.process = process;
		}

		protected StackFrame CurrentFrame {
			get { return manager.CurrentFrame; }
		}

		// <remarks>
		//   This method will always get called from the gtk# thread and while keeping the
		//   `this' lock.
		// </remarks>
		protected virtual void OnTargetExited ()
		{
			process = null;
		}

		// <remarks>
		//   This method will always get called from the gtk# thread and while keeping the
		//   `this' lock.
		// </remarks>
		protected virtual void OnTargetEvent (TargetEventArgs args)
		{
		}

		void target_event (object sender, TargetEventArgs args)
		{
			OnTargetEvent (args);

			if ((args.Type == TargetEventType.TargetExited) ||
			    (args.Type == TargetEventType.TargetSignaled))
				OnTargetExited ();
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
