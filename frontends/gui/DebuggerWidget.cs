using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public abstract class DebuggerWidget
	{
		protected Gtk.Widget widget;
		protected IDebuggerBackend backend;

		[DllImport("glib-2.0")]
		static extern bool g_main_context_iteration (IntPtr context, bool may_block);

		public DebuggerWidget (IDebuggerBackend backend, Gtk.Widget widget)
		{
			this.widget = widget;
			this.backend = backend;
		}

		public virtual Gtk.Widget Widget {
			get {
				return widget;
			}
		}

		public virtual IDebuggerBackend Debugger {
			get {
				return backend;
			}
		}

                protected void MainIteration ()
		{
			while (g_main_context_iteration (IntPtr.Zero, false))
				;
		}
	}
}
