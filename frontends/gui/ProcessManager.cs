using GLib;
using Gtk;
using GtkSharp;
using System;
using System.IO;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;

namespace Mono.Debugger.GUI
{
	public class ProcessManager : DebuggerWidget
	{
		Gtk.TreeView tree;
		Gtk.ListStore store;
		int notify_id;

		public ProcessManager (DebuggerGUI gui, Gtk.Container window, Gtk.Container container)
			: base (gui, window, container)
		{
			store = new ListStore ((int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeBoolean);

			tree = new TreeView (store);

			tree.HeadersVisible = true;

			// FIXME: I don't know how to add tooltips.

			TreeViewColumn IdCol = new TreeViewColumn ();
			CellRenderer IdRenderer = new CellRendererText ();
			IdCol.Title = "ID";
			IdCol.PackStart (IdRenderer, true);
			IdCol.AddAttribute (IdRenderer, "text", 0);
			tree.AppendColumn (IdCol);

			TreeViewColumn StateCol = new TreeViewColumn ();
			CellRenderer StateRenderer = new CellRendererText ();
			StateCol.Title = "State";
			StateCol.PackStart (StateRenderer, false);
			StateCol.AddAttribute (StateRenderer, "text", 1);
			tree.AppendColumn (StateCol);

			TreeViewColumn RunningCol = new TreeViewColumn ();
			CellRendererToggle RunningRenderer = new CellRendererToggle ();
			RunningRenderer.Activatable = true;
			RunningRenderer.Toggled += new ToggledHandler (running_toggled);
			RunningCol.Title = "Running";
			RunningCol.PackStart (RunningRenderer, false);
			RunningCol.AddAttribute (RunningRenderer, "active", 2);
			tree.AppendColumn (RunningCol);

			container.Add (tree);
			container.ShowAll ();

			notify_id = thread_notify.RegisterListener (new ReadyEventHandler (reload_event));

			backend.ThreadManager.InitializedEvent += new ThreadEventHandler (manager_initialized);
			backend.ThreadManager.ThreadCreatedEvent += new ThreadEventHandler (thread_created);
		}

		void manager_initialized (ThreadManager manager, Process process)
		{
			TreeIter iter = new TreeIter ();

			store.Append (out iter);
			store.SetValue (iter, 0, new GLib.Value (process.ID.ToString ()));
			store.SetValue (iter, 1, new GLib.Value ("FOREGROUND"));
		}

		void thread_created (ThreadManager manager, Process process)
		{
			process.StateChanged += new StateChangedHandler (real_state_changed);

			lock (this) {
				thread_notify.Signal (notify_id);
			}
		}

		void add_process (Process process)
		{
			TreeIter iter = new TreeIter ();

			string state;
			if (process == Process)
				// This is the process we're currently debugging in this GUI.
				state = "FOREGROUND";
			else
				state = process.State.ToString ();

			store.Append (out iter);
			store.SetValue (iter, 0, new GLib.Value (process.ID.ToString ()));
			store.SetValue (iter, 1, new GLib.Value (state));
			store.SetValue (iter, 2, new GLib.Value (process.State == TargetState.RUNNING));
		}

		Process[] processes = null;

		void running_toggled (object sender, GtkSharp.ToggledArgs args)
		{
			if (processes == null)
				return;

			Process process = processes [Int32.Parse (args.Path)];
			if (!process.CanRun || (process.ID < 3))
				return;

			if (process.IsStopped)
				process.Continue (true, false);
			else
				process.Stop ();
		}

		void reload_event ()
		{
			lock (this) {
				if (!IsVisible)
					return;

				store.Clear ();

				processes = backend.ThreadManager.Threads;
				foreach (Process process in processes)
					add_process (process);
			}
		}

		void real_state_changed (TargetState state, int arg)
		{
			lock (this) {
				thread_notify.Signal (notify_id);
			}
		}
	}
}
