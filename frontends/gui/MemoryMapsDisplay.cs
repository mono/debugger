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
	public class MemoryMapsDisplay : DebuggerWidget
	{
		Gtk.TreeView tree;
		Gtk.ListStore store;

		public MemoryMapsDisplay (DebuggerGUI gui, Gtk.Container window, Gtk.Container container)
			: base (gui, window, container)
		{
			store = new ListStore ((int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeBoolean,
					       (int)TypeFundamentals.TypeString);

			tree = new TreeView (store);

			tree.HeadersVisible = true;

			TreeViewColumn StartCol = new TreeViewColumn ();
			CellRenderer StartRenderer = new CellRendererText ();
			StartCol.Title = "Start";
			StartCol.PackStart (StartRenderer, false);
			StartCol.AddAttribute (StartRenderer, "text", 0);
			tree.AppendColumn (StartCol);

			TreeViewColumn EndCol = new TreeViewColumn ();
			CellRenderer EndRenderer = new CellRendererText ();
			EndCol.Title = "End";
			EndCol.PackEnd (EndRenderer, false);
			EndCol.AddAttribute (EndRenderer, "text", 1);
			tree.AppendColumn (EndCol);

			TreeViewColumn ReadOnlyCol = new TreeViewColumn ();
			CellRendererToggle ReadOnlyRenderer = new CellRendererToggle ();
			ReadOnlyCol.Title = "W";
			ReadOnlyCol.PackStart (ReadOnlyRenderer, false);
			ReadOnlyCol.AddAttribute (ReadOnlyRenderer, "active", 2);
			tree.AppendColumn (ReadOnlyCol);

			TreeViewColumn NameCol = new TreeViewColumn ();
			CellRendererText NameRenderer = new CellRendererText ();
			NameCol.Title = "Name";
			NameCol.PackStart (NameRenderer, false);
			NameCol.AddAttribute (NameRenderer, "text", 3);
			tree.AppendColumn (NameCol);

			container.Add (tree);
			container.ShowAll ();
		}

		public override void SetProcess (Process process)
		{
			base.SetProcess (process);

			backend.ModulesChangedEvent += new ModulesChangedHandler (modules_changed);
		}

		TargetMemoryArea[] memory_maps = null;

		void add_area (TargetMemoryArea area)
		{
			TreeIter iter = new TreeIter ();

			bool writable = process.TargetMemoryAccess.CanWrite &&
				((area.Flags & TargetMemoryFlags.ReadOnly) == 0);

			store.Append (out iter);
			store.SetValue (iter, 0, new GLib.Value (area.Start.ToString ()));
			store.SetValue (iter, 1, new GLib.Value (area.End.ToString ()));
			store.SetValue (iter, 2, new GLib.Value (writable));
			store.SetValue (iter, 3, new GLib.Value (area.Name));
		}

		void modules_changed ()
		{
			if (!IsVisible)
				return;

			store.Clear ();

			try {
				memory_maps = process.GetMemoryMaps ();
			} catch {
				return;
			}

			foreach (TargetMemoryArea area in memory_maps)
				add_area (area);
		}
	}
}
