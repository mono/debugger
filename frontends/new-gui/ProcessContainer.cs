using System;
using Gtk;
using GtkSharp;
using Gnome;

using Mono.Debugger;
using Mono.Debugger.Frontends.CommandLine;

namespace Mono.Debugger.GUI
{
	public class ProcessContainer
	{
		VBox vbox;
		Process process;
		GUIContext context;

		TargetStatusbar status;
		BacktraceView backtrace;

		public ProcessContainer (GUIContext context, Process process)
		{
			this.context = context;
			this.process = process;

			vbox = new VBox (false, 8);

			backtrace = new BacktraceView (this);
			vbox.PackStart (backtrace.Widget, true, true, 0);

			status = new TargetStatusbar (this);
			vbox.PackEnd (status.Widget, false, true, 0);
		}

		public GUIContext Context {
			get { return context; }
		}

		public Process Process {
			get { return process; }
		}

		public Widget Widget {
			get { return vbox; }
		}
	}
}
