using GLib;
using Gtk;
using GtkSharp;
using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;

namespace Mono.Debugger.GUI
{
	public class HexEditor : DebuggerWidget
	{
		Glade.XML gxml;
		Gtk.TreeView tree;
		Gtk.ListStore store;
		Gtk.Entry address_entry, size_entry;
		Gtk.Button up_button, down_button, close_button;

		TreeViewColumn AddressCol, DataCol, TextCol;

		public HexEditor (Glade.XML gxml, Gtk.Container window, Gtk.Container container)
			: base (window, container)
		{
			this.gxml = gxml;

			store = new ListStore ((int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString);

			tree = new TreeView (store);

			tree.HeadersVisible = true;

			AddressCol = new TreeViewColumn ();
			CellRenderer AddressRenderer = new CellRendererText ();
			AddressCol.Title = "Address";
			AddressCol.PackStart (AddressRenderer, false);
			AddressCol.AddAttribute (AddressRenderer, "text", 0);
			tree.AppendColumn (AddressCol);

			DataCol = new TreeViewColumn ();
			CellRenderer DataRenderer = new CellRendererText ();
			DataCol.Title = "Data";
			DataCol.PackStart (DataRenderer, false);
			DataCol.AddAttribute (DataRenderer, "text", 1);
			tree.AppendColumn (DataCol);

			TextCol = new TreeViewColumn ();
			CellRenderer TextRenderer = new CellRendererText ();
			TextCol.Title = "Text";
			TextCol.PackStart (TextRenderer, false);
			TextCol.AddAttribute (TextRenderer, "text", 2);
			tree.AppendColumn (TextCol);

			container.Add (tree);
			container.ShowAll ();

			address_entry = (Entry) gxml ["hexeditor-address"];
			address_entry.Activated += new EventHandler (activated_handler);

			size_entry = (Entry) gxml ["hexeditor-size"];
			size_entry.Activated += new EventHandler (activated_handler);

			up_button = (Button) gxml ["hexeditor-up"];
			up_button.Clicked += new EventHandler (up_activated);

			down_button = (Button) gxml ["hexeditor-down"];
			down_button.Clicked += new EventHandler (down_activated);

			close_button = (Button) gxml ["hexeditor-close"];
			close_button.Clicked += new EventHandler (close_activated);
		}

		void close_activated (object sender, EventArgs args)
		{
			Hide ();
		}

		void up_activated (object sender, EventArgs args)
		{
			if (start.IsNull)
				return;

			TreeIter iter;
			store.Prepend (out iter);
			start -= 16;
			add_line (iter, start);

			size_entry.Text = String.Format ("0x{0:x}", end - start);

			TreePath path = store.GetPath (iter);
			tree.SetCursor (path, AddressCol, false);
			tree.ScrollToCell (path, AddressCol, false, 0.0F, 0.0F);
		}

		void down_activated (object sender, EventArgs args)
		{
			if (start.IsNull)
				return;

			TreeIter iter;
			store.Append (out iter);
			add_line (iter, end);
			end += 16;

			size_entry.Text = String.Format ("0x{0:x}", end - start);

			TreePath path = store.GetPath (iter);
			tree.SetCursor (path, AddressCol, false);
			tree.ScrollToCell (path, AddressCol, false, 0.0F, 0.0F);
		}

		void activated_handler (object sender, EventArgs args)
		{
			long address, size;
			try {
				string text = address_entry.Text;

				if (text.StartsWith ("0x"))
					address = Int64.Parse (text.Substring (2), NumberStyles.HexNumber);
				else
					address = Int64.Parse (text);
			} catch {
				Console.WriteLine ("Invalid number in address field!");
				return;
			}

			try {
				string text = size_entry.Text;

				if (text.StartsWith ("0x"))
					size = Int64.Parse (text.Substring (2), NumberStyles.HexNumber);
				else
					size = Int64.Parse (text);
			} catch {
				Console.WriteLine ("Invalid number in size field!");
				return;
			}

			try {
				start = new TargetAddress (backend.Inferior, address);
				end = start + size;
				UpdateDisplay ();
			} catch (Exception e) {
				Console.WriteLine ("CAN'T UPDATE DISPLAY: {0}", e);
			}
		}

		TargetAddress start = TargetAddress.Null;
		TargetAddress end = TargetAddress.Null;

		void UpdateDisplay ()
		{
			store.Clear ();

			for (TargetAddress ptr = start; ptr < end; ptr += 16) {
				TreeIter iter;
				store.Append (out iter);
				add_line (iter, ptr);
			}

			size_entry.Text = String.Format ("0x{0:x}", end - start);
		}

		void add_line (TreeIter iter, TargetAddress address)
		{
			StringBuilder sb = new StringBuilder ();

			char[] data = new char [16];

			for (int i = 0 ; i < 16; i++) {
				try {
					byte b = backend.TargetMemoryAccess.ReadByte (address + i);
					sb.Append (String.Format ("{1}{0:x} ", b, b >= 16 ? "" : "0"));
					if (b > 0x20)
						data [i] = (char) b;
					else
						data [i] = '.';
				} catch {
					sb.Append ("   ");
					data [i] = ' ';
				}

				if (i == 8)
					sb.Append ("- ");
			}

			store.SetValue (iter, 0, new GLib.Value (String.Format ("{0:x}  ", address)));
			store.SetValue (iter, 1, new GLib.Value (sb.ToString ()));
			store.SetValue (iter, 2, new GLib.Value (new String (data)));
		}

		public override void SetBackend (DebuggerBackend backend)
		{
			base.SetBackend (backend);
		}
	}
}
