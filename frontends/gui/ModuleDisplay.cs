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
	public class ModuleDisplay : DebuggerWidget
	{
		Gtk.TreeView tree;
		Gtk.ListStore store;

		public ModuleDisplay (DebuggerGUI gui, string glade_name)
			: this (gui, (Gtk.Container) gui.GXML [glade_name])
		{ }

		public ModuleDisplay (DebuggerGUI gui, Gtk.Container container)
			: base (gui, container)
		{
			store = new ListStore ((int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeBoolean,
					       (int)TypeFundamentals.TypeBoolean,
					       (int)TypeFundamentals.TypeBoolean,
					       (int)TypeFundamentals.TypeBoolean);

			tree = new TreeView (store);

			tree.HeadersVisible = true;

			// FIXME: I don't know how to add tooltips.

			TreeViewColumn NameCol = new TreeViewColumn ();
			CellRenderer NameRenderer = new CellRendererText ();
			NameCol.Title = "Name";
			NameCol.PackStart (NameRenderer, true);
			NameCol.AddAttribute (NameRenderer, "text", 0);
			tree.AppendColumn (NameCol);

			TreeViewColumn LoadedCol = new TreeViewColumn ();
			CellRendererToggle LoadedRenderer = new CellRendererToggle ();
			LoadedCol.Title = "Loaded";
			// TOOLTIP: Shows whether this module is currently loaded.
			LoadedCol.PackStart (LoadedRenderer, false);
			LoadedCol.AddAttribute (LoadedRenderer, "active", 1);
			tree.AppendColumn (LoadedCol);

			TreeViewColumn SymbolsCol = new TreeViewColumn ();
			CellRendererToggle SymbolsRenderer = new CellRendererToggle ();
			SymbolsCol.Title = "Symbols";
			// TOOLTIP: Shows whether the symbol file for this this module is currently loaded.
			SymbolsCol.PackStart (SymbolsRenderer, false);
			SymbolsCol.AddAttribute (SymbolsRenderer, "active", 2);
			tree.AppendColumn (SymbolsCol);

			TreeViewColumn StepIntoCol = new TreeViewColumn ();
			CellRendererToggle StepIntoRenderer = new CellRendererToggle ();
			StepIntoRenderer.Activatable = true;
			StepIntoRenderer.Toggled += new ToggledHandler (step_into_toggled);
			StepIntoCol.Title = "Step";
			// TOOLTIP: This may be modified by the user.
			//          When checked, the debugger will enter methods of this module while
			//          single-stepping if they have debugging info.  The debugger will still
			//          load this module's symbol file if the applications crashes or hits
			//          a breakpoint somewhere in this module.
			StepIntoCol.PackStart (StepIntoRenderer, false);
			StepIntoCol.AddAttribute (StepIntoRenderer, "active", 3);
			tree.AppendColumn (StepIntoCol);

			TreeViewColumn LoadSymbolsCol = new TreeViewColumn ();
			CellRendererToggle LoadSymbolsRenderer = new CellRendererToggle ();
			LoadSymbolsRenderer.Activatable = true;
			LoadSymbolsRenderer.Toggled += new ToggledHandler (load_symbols_toggled);
			LoadSymbolsCol.Title = "Ignore";
			// TOOLTIP: This may be modified by the user.
			//          When checked, the debugger will ignore this module;  it'll neither
			//          step into it nor read its symbol file if it the application crashes
			//          or hits a breakpoint somewhere inside this module.
			LoadSymbolsCol.PackStart (LoadSymbolsRenderer, false);
			LoadSymbolsCol.AddAttribute (LoadSymbolsRenderer, "active", 4);
			tree.AppendColumn (LoadSymbolsCol);

			container.Add (tree);
			container.ShowAll ();
		}

		protected override void SetBackend (DebuggerBackend backend)
		{
			base.SetBackend (backend);

			backend.ModulesChangedEvent += new ModulesChangedHandler (modules_changed);
		}

		Module[] modules = null;

		void load_symbols_toggled (object sender, GtkSharp.ToggledArgs args)
		{
			Module module = modules [Int32.Parse (args.Path)];
			module.LoadSymbols = ! module.LoadSymbols;
		}

		void step_into_toggled (object sender, GtkSharp.ToggledArgs args)
		{
			Module module = modules [Int32.Parse (args.Path)];
			module.StepInto = ! module.StepInto;
		}

		void add_module (Module module)
		{
			TreeIter iter = new TreeIter ();

			store.Append (out iter);
			store.SetValue (iter, 0, new GLib.Value (Path.GetFileName (module.Name)));
			store.SetValue (iter, 1, new GLib.Value (module.IsLoaded));
			store.SetValue (iter, 2, new GLib.Value (module.SymbolsLoaded));
			store.SetValue (iter, 3, new GLib.Value (module.StepInto));
			store.SetValue (iter, 4, new GLib.Value (!module.LoadSymbols));
		}

		void modules_changed ()
		{
			if (!IsVisible)
				return;

			store.Clear ();

			modules = backend.Modules;

			foreach (Module module in modules)
				add_module (module);
		}
	}
}
