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
	public class VariableDisplay : DebuggerWidget
	{
		new StackFrame current_frame;

		Gtk.TreeView tree;
		Gtk.TreeStore store;
		bool is_locals_display;

		public VariableDisplay (DebuggerGUI gui, string glade_name, bool is_locals_display)
			: this (gui, (Gtk.Container) gui.GXML [glade_name], is_locals_display)
		{ }

		public VariableDisplay (DebuggerGUI gui, Gtk.Container container, bool is_locals_display)
			: base (gui, null, container)
		{
			this.is_locals_display = is_locals_display;

			store = new TreeStore ((int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString);

			tree = new TreeView (store);
			tree.RulesHint = true;
			tree.HeadersVisible = true;

			TreeViewColumn NameCol = new TreeViewColumn ();
			CellRenderer NameRenderer = new CellRendererText ();
			NameCol.Title = "Name";
			NameCol.PackStart (NameRenderer, true);
			NameCol.AddAttribute (NameRenderer, "text", 0);
			NameCol.Resizable = true;
			NameCol.Alignment = 0.0f;
			tree.AppendColumn (NameCol);

			TreeViewColumn TypeCol = new TreeViewColumn ();
			CellRenderer TypeRenderer = new CellRendererText ();
			TypeCol.Title = "Type";
			TypeCol.PackStart (TypeRenderer, true);
			TypeCol.AddAttribute (TypeRenderer, "text", 1);
			TypeCol.Resizable = true;
			NameCol.Alignment = 0.0f;
			tree.AppendColumn (TypeCol);

			TreeViewColumn ValueCol = new TreeViewColumn ();
			CellRenderer ValueRenderer = new CellRendererText ();
			ValueCol.Title = "Value";
			ValueCol.PackStart (ValueRenderer, true);
			ValueCol.AddAttribute (ValueRenderer, "text", 2);
			ValueCol.Resizable = true;
			NameCol.Alignment = 0.0f;
			tree.AppendColumn (ValueCol);

			tree.TestExpandRow += new TestExpandRowHandler (test_expand_row);

			container.Add (tree);
			container.ShowAll ();
		}

		bool add_array (TreeIter parent, ITargetArrayObject array)
		{
			bool inserted = false;

			for (int i = array.LowerBound; i < array.UpperBound; i++) {
				ITargetObject elt = array [i];
				if (elt == null)
					continue;

				TreeIter iter;
				store.Append (out iter, parent);
				add_object (elt, i.ToString (), iter);
				inserted = true;
			}

			return inserted;
		}

		bool add_struct (TreeIter parent, ITargetStructObject sobj)
		{
			bool inserted = false;

			foreach (ITargetFieldInfo field in sobj.Type.Fields) {
				TreeIter iter;
				store.Append (out iter, parent);
				add_object (sobj.GetField (field.Index), field.Name, iter);
				inserted = true;
			}

			return inserted;
		}

		bool add_class (TreeIter parent, ITargetClassObject sobj)
		{
			bool inserted = false;

			if (sobj.Type.HasParent) {
				TreeIter iter;
				store.Append (out iter, parent);
				add_object (sobj.Parent, "<parent>", iter);
				inserted = true;
			}

			if (add_struct (parent, sobj))
				inserted = true;

			return inserted;
		}

		void add_message (TreeIter parent, string message)
		{
			TreeIter child;
			if (store.IterChildren (out child, parent)) {
				while (child != Gtk.TreeIter.Zero && (child.stamp != 0))
					store.Remove (out child);
			}

			TreeIter iter;
			store.Append (out iter, parent);
			store.SetValue (iter, 2, new GLib.Value (message));
		}

		void test_expand_row (object o, TestExpandRowArgs args)
		{
			bool inserted = false;

			ITargetObject obj = (ITargetObject) iters [args.Iter];

			TreeIter child;
			if (store.IterChildren (out child, args.Iter)) {
				while (child != Gtk.TreeIter.Zero && (child.stamp != 0))
					store.Remove (out child);
			}

			if (obj == null) {
				store.Append (out child, args.Iter);
				return;
			}

			switch (obj.Type.Kind) {
			case TargetObjectKind.Array:
				ITargetArrayObject array = (ITargetArrayObject) obj;
				try {
					inserted = add_array (args.Iter, array);
				} catch {
					add_message (args.Iter, "<can't display array>");
					inserted = true;
				}
				if (!inserted)
					add_message (args.Iter, "<empty array>");
				break;

			case TargetObjectKind.Class:
				ITargetClassObject cobj = (ITargetClassObject) obj;
				try {
					inserted = add_class (args.Iter, cobj);
				} catch {
					add_message (args.Iter, "<can't display class>");
					inserted = true;
				}
				if (!inserted)
					add_message (args.Iter, "<empty class>");
				break;

			case TargetObjectKind.Struct:
				ITargetStructObject sobj = (ITargetStructObject) obj;
				try {
					inserted = add_struct (args.Iter, sobj);
				} catch {
					add_message (args.Iter, "<can't display struct>");
					inserted = true;
				}
				if (!inserted)
					add_message (args.Iter, "<empty struct>");
				break;

			default:
				add_message (args.Iter, "<unknown object>");
				break;
			}
		}

		void add_data (ITargetObject obj, TreeIter parent)
		{
			TreeIter iter;
			store.Append (out iter, parent);
			iters.Add (parent, obj);
		}

		void add_object (ITargetObject obj, string name, TreeIter iter)
		{
			store.SetValue (iter, 0, new GLib.Value (name));
			store.SetValue (iter, 1, new GLib.Value (obj.Type.Name));

			switch (obj.Type.Kind) {
			case TargetObjectKind.Fundamental:
				object contents = ((ITargetFundamentalObject) obj).Object;
				store.SetValue (iter, 2, new GLib.Value (contents.ToString ()));
				break;

			case TargetObjectKind.Array:
			case TargetObjectKind.Struct:
			case TargetObjectKind.Class:
				add_data (obj, iter);
				break;
			}
		}

		void add_variable (IVariable variable)
		{
			if (!variable.IsAlive (current_frame.TargetAddress))
				return;

			TreeIter iter;
			store.Append (out iter);

			try {
				ITargetObject obj = variable.GetObject (current_frame);
				add_object (obj, variable.Name, iter);
			} catch (LocationInvalidException) {
				// Do nothing
			} catch (Exception e) {
				Console.WriteLine ("CAN'T ADD VARIABLE: {0} {1}", variable, e);
			}
		}

		Hashtable iters = null;

		public void UpdateDisplay ()
		{
			if (!IsVisible)
				return;

			store.Clear ();
			iters = new Hashtable ();

			if ((current_frame == null) || (current_frame.Method == null))
				return;

			try {
				if (is_locals_display) {
					IVariable[] local_vars = current_frame.Method.Locals;
					foreach (IVariable var in local_vars)
						add_variable (var);
				} else {
					IVariable[] param_vars = current_frame.Method.Parameters;
					foreach (IVariable var in param_vars)
						add_variable (var);
				}
			} catch (Exception e) {
				Console.WriteLine ("CAN'T GET VARIABLES: {0}", e);
				store.Clear ();
				iters = new Hashtable ();
			}
		}
		
		protected override void FrameChanged (StackFrame frame)
		{
			current_frame = frame;

			if (!process.HasTarget)
				return;

			UpdateDisplay ();
		}

		protected override void FramesInvalid ()
		{
			current_frame = null;
		}
	}
}
