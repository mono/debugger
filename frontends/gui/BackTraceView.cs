using GLib;
using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public class BackTraceView : DebuggerWidget
	{
		Gtk.TreeView tree;
		Gtk.ListStore store;

		public BackTraceView (DebuggerGUI gui, string glade_name)
			: this (gui, (Gtk.Container) gui.GXML [glade_name])
		{ }

		public BackTraceView (DebuggerGUI gui, Gtk.Container container)
			: base (gui, container)
		{
			store = new ListStore ((int)TypeFundamentals.TypeInt,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString);

			tree = new TreeView (store);

			tree.HeadersVisible = true;
			tree.RulesHint = true;
			TreeViewColumn IdCol = new TreeViewColumn ();
			CellRenderer IdRenderer = new CellRendererText ();
			IdCol.Title = "#ID";
			IdCol.PackStart (IdRenderer, true);
			IdCol.AddAttribute (IdRenderer, "text", 0);
			tree.AppendColumn (IdCol);

			TreeViewColumn AddressCol = new TreeViewColumn ();
			CellRenderer AddressRenderer = new CellRendererText ();
			AddressCol.Title = "Address";
			AddressCol.PackStart (AddressRenderer, false);
			AddressCol.AddAttribute (AddressRenderer, "text", 1);
			tree.AppendColumn (AddressCol);

			TreeViewColumn MethodCol = new TreeViewColumn ();
			CellRenderer MethodRenderer = new CellRendererText ();
			MethodCol.Title = "Method";
			MethodCol.PackStart (MethodRenderer, false);
			MethodCol.AddAttribute (MethodRenderer, "text", 2);
			tree.AppendColumn (MethodCol);

			TreeViewColumn LocationCol = new TreeViewColumn ();
			CellRenderer LocationRenderer = new CellRendererText ();
			LocationCol.Title = "Location";
			LocationCol.PackStart (LocationRenderer, false);
			LocationCol.AddAttribute (LocationRenderer, "text", 3);
			tree.AppendColumn (LocationCol);

			container.Add (tree);
			container.ShowAll ();
		}

		protected override void StateChanged (TargetState new_state, int arg)
		{
			if (!IsVisible)
				return;

			store.Clear ();
			Backtrace bt = manager.CurrentBacktrace;
			if (bt == null)
				return;

			switch (new_state) {
			case TargetState.STOPPED:
				for (int i = 0; i < bt.Length; i++)
					add_frame (i, bt [i]);
				break;
			}
		}

		void add_frame (int id, StackFrame frame)
		{
			TreeIter iter = new TreeIter ();

			store.Append (out iter);
			store.SetValue (iter, 0, new GLib.Value (id));
			store.SetValue (iter, 1, new GLib.Value (frame.TargetAddress.ToString ()));
			if (frame.Name != null)
				store.SetValue (iter, 2, new GLib.Value (frame.Name));
			if (frame.SourceAddress != null) {
				string filename = Utils.GetBasename (frame.SourceAddress.Name);
				store.SetValue (iter, 3, new GLib.Value (filename));
			}
		}

	}
}
