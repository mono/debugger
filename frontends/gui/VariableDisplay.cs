using GLib;
using Gtk;
using GtkSharp;
using System;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;

namespace Mono.Debugger.GUI
{
	public class VariableDisplay : DebuggerWidget
	{
		Glade.XML gxml;
		StackFrame current_frame;

		Gtk.TreeView tree;
		Gtk.TreeStore store;

		public VariableDisplay (Glade.XML gxml, Gtk.Container window, Gtk.Container container)
			: base (window, container)
		{
			this.gxml = gxml;

			store = new TreeStore ((int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString);

			tree = new TreeView (store);

			tree.HeadersVisible = true;

			TreeViewColumn NameCol = new TreeViewColumn ();
			CellRenderer NameRenderer = new CellRendererText ();
			NameCol.Title = "Name";
			NameCol.PackStart (NameRenderer, true);
			NameCol.AddAttribute (NameRenderer, "text", 0);
			tree.AppendColumn (NameCol);

			TreeViewColumn TypeCol = new TreeViewColumn ();
			CellRenderer TypeRenderer = new CellRendererText ();
			TypeCol.Title = "Type";
			TypeCol.PackStart (TypeRenderer, true);
			TypeCol.AddAttribute (TypeRenderer, "text", 1);
			tree.AppendColumn (TypeCol);

			TreeViewColumn ValueCol = new TreeViewColumn ();
			CellRenderer ValueRenderer = new CellRendererText ();
			ValueCol.Title = "Value";
			ValueCol.PackStart (ValueRenderer, true);
			ValueCol.AddAttribute (ValueRenderer, "text", 2);
			tree.AppendColumn (ValueCol);

			tree.TestExpandRow += new TestExpandRowHandler (test_expand_row);

			container.Add (tree);
			container.ShowAll ();
		}

		void test_expand_row (object o, TestExpandRowArgs args)
		{
			Console.WriteLine ("EVENT");
			// store.SetValue (args.Iter, 0, new GLib.Value ("Hello"));
		}

		public override void SetBackend (DebuggerBackend backend)
		{
			base.SetBackend (backend);

			backend.FrameChangedEvent += new StackFrameHandler (FrameChangedEvent);
			backend.FramesInvalidEvent += new StackFrameInvalidHandler (FramesInvalidEvent);
		}

		void add_data (IVariable variable, TreeIter parent)
		{
			TreeIter iter = new TreeIter ();

			store.Append (out iter, parent);
		}

		void add_variable (IVariable variable)
		{
			TreeIter iter = new TreeIter ();

			Console.WriteLine ("ADD VARIABLE: {0} {1} {2}", variable.Name, variable.Type.Name,
					   variable.Type.HasObject);

			store.Append (out iter);
			store.SetValue (iter, 0, new GLib.Value (variable.Name));
			store.SetValue (iter, 1, new GLib.Value (variable.Type.Name));

			ITargetObject obj = variable.GetObject (current_frame);

			Console.WriteLine ("OBJECT: {0} {1}", obj, obj.HasObject);
			if (obj.HasObject) {
				object contents = obj.Object;
				store.SetValue (iter, 2, new GLib.Value (contents.ToString ()));
			}

			// add_data (variable, iter);
		}

		public void UpdateDisplay ()
		{
			if (!IsVisible)
				return;
			
			store.Clear ();

			if ((current_frame == null) || (current_frame.Method == null))
				return;

			try {
				IVariable[] vars = current_frame.Method.Parameters;
				if (vars.Length == 0)
					return;

				foreach (IVariable var in vars)
					add_variable (var);
			} catch {
				store.Clear ();
			}
		}
		
		void FrameChangedEvent (StackFrame frame)
		{
			current_frame = frame;

			if (!backend.HasTarget)
				return;

			UpdateDisplay ();
		}

		void FramesInvalidEvent ()
		{
			current_frame = null;
		}
	}
}
