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
	public class BreakpointManager : DebuggerWidget
	{
		Gtk.TreeView tree;
		Gtk.ListStore store;

		public BreakpointManager (DebuggerGUI gui, Gtk.Container window, Gtk.Container container)
			: base (gui, window, container)
		{
			store = new ListStore ((int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeBoolean);

			tree = new TreeView (store);

			tree.HeadersVisible = true;
			tree.RulesHint = true;
			
			TreeViewColumn IdCol = new TreeViewColumn ();
			CellRenderer IdRenderer = new CellRendererText ();
			IdCol.Title = "ID";
			IdCol.PackStart (IdRenderer, false);
			IdCol.AddAttribute (IdRenderer, "text", 0);
			tree.AppendColumn (IdCol);

			TreeViewColumn EnabledCol = new TreeViewColumn ();
			CellRendererToggle EnabledRenderer = new CellRendererToggle ();
			EnabledRenderer.Activatable = true;
			EnabledRenderer.Toggled += new ToggledHandler (enabled_toggled);
			EnabledCol.Title = "Enabled";
			EnabledCol.PackStart (EnabledRenderer, false);
			EnabledCol.AddAttribute (EnabledRenderer, "active", 2);
			tree.AppendColumn (EnabledCol);

			TreeViewColumn NameCol = new TreeViewColumn ();
			CellRenderer NameRenderer = new CellRendererText ();
			NameCol.Title = "Name";
			NameCol.PackStart (NameRenderer, false);
			NameCol.AddAttribute (NameRenderer, "text", 1);
			tree.AppendColumn (NameCol);

			container.Add (tree);
			container.ShowAll ();
		}

		protected override void SetProcess (Process process)
		{
			base.SetProcess (process);

			backend.ModulesChangedEvent += new ModulesChangedHandler (breakpoints_changed);
			backend.BreakpointsChangedEvent += new BreakpointsChangedHandler (breakpoints_changed);
		}

		void enabled_toggled (object sender, GtkSharp.ToggledArgs args)
		{
			Breakpoint breakpoint = (Breakpoint) breakpoints [Int32.Parse (args.Path)];
			breakpoint.Enabled = ! breakpoint.Enabled;
		}

		ArrayList breakpoints = null;

		void breakpoints_changed ()
		{
			if (!IsVisible)
				return;

			store.Clear ();

			breakpoints = new ArrayList ();

			try {
				foreach (Module module in backend.Modules)
					breakpoints.AddRange (module.Breakpoints);
			} catch {
				return;
			}

			for (int i = 0; i < breakpoints.Count; i++)
				add_breakpoint ((Breakpoint) breakpoints [i]);
		}

		void add_breakpoint (Breakpoint breakpoint)
		{
			TreeIter iter = new TreeIter ();

			store.Append (out iter);
			store.SetValue (iter, 0, new GLib.Value (breakpoint.Index.ToString ()));
			store.SetValue (iter, 1, new GLib.Value (breakpoint.Name));
			store.SetValue (iter, 2, new GLib.Value (breakpoint.Enabled));
		}
	}
}
