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
		Gtk.Entry address_entry, size_entry, area_entry;
		Gtk.Button up_button, down_button, close_button;
		Gtk.ToggleButton force_writable_button;

		TreeViewColumn AddressCol;
		TreeViewColumn[] DataCol;
		CellRendererText[] DataRenderer;
		Combo area_combo;
		bool force_writable;

		public HexEditor (Glade.XML gxml, Gtk.Container window, Gtk.Container container)
			: base (window, container)
		{
			this.gxml = gxml;

			store = new ListStore ((int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString,
					       (int)TypeFundamentals.TypeString);

			tree = new TreeView (store);

			tree.HeadersVisible = true;

			AddressCol = new TreeViewColumn ();
			CellRenderer AddressRenderer = new CellRendererText ();
			AddressCol.Title = "Address";
			AddressCol.PackStart (AddressRenderer, false);
			AddressCol.AddAttribute (AddressRenderer, "text", 16);
			tree.AppendColumn (AddressCol);

			DataRenderer = new CellRendererText [16];
			DataCol = new TreeViewColumn [16];

			for (int i = 0; i < 16; i++) {
				DataCol [i] = new TreeViewColumn ();
				DataRenderer [i] = new CellRendererText ();
				DataRenderer [i].Edited += new EditedHandler (edited_handler);
				DataCol [i].Title = String.Format ("0{0:X}", i);
				DataCol [i].PackStart (DataRenderer [i], false);
				DataCol [i].AddAttribute (DataRenderer [i], "text", i);
				tree.AppendColumn (DataCol [i]);

				if (i == 8) {
					TreeViewColumn SepCol = new TreeViewColumn ();
					CellRenderer SepRenderer = new CellRendererText ();
					SepCol.Title = "-";
					SepCol.PackStart (SepRenderer, false);
					SepCol.AddAttribute (SepRenderer, "text", 17);
					tree.AppendColumn (SepCol);
				}
			}

			TreeViewColumn TextCol = new TreeViewColumn ();
			CellRenderer TextRenderer = new CellRendererText ();
			TextCol.Title = "Text";
			TextCol.PackStart (TextRenderer, false);
			TextCol.AddAttribute (TextRenderer, "text", 18);
			tree.AppendColumn (TextCol);

			container.Add (tree);
			container.ShowAll ();

			address_entry = (Entry) gxml ["hexeditor-address"];
			address_entry.Activated += new EventHandler (activated_handler);

			size_entry = (Entry) gxml ["hexeditor-size"];
			size_entry.Activated += new EventHandler (activated_handler);

			area_entry = (Entry) gxml ["hexeditor-memory-area"];
			area_entry.Activated += new EventHandler (area_activated);
			area_combo = (Combo) gxml ["hexeditor-memory-areas"];
			area_combo.DisableActivate ();

			up_button = (Button) gxml ["hexeditor-up"];
			up_button.Clicked += new EventHandler (up_activated);

			down_button = (Button) gxml ["hexeditor-down"];
			down_button.Clicked += new EventHandler (down_activated);

			close_button = (Button) gxml ["hexeditor-close"];
			close_button.Clicked += new EventHandler (close_activated);

			// When checked, the editor will allow writing read-only memory pages.
			// The write operation may or may not succeed and an error dialog will be
			// displayed to the user if it failed.
			force_writable_button = (ToggleButton) gxml ["hexeditor-force-writable"];
			force_writable_button.Toggled += new EventHandler (force_writable_toggled);
		}

		void force_writable_toggled (object sender, EventArgs args)
		{
			force_writable = !force_writable;
			UpdateDisplay ();
		}

		void edited_handler (object sender, EditedArgs args)
		{
			int offset = -1;
			for (int i = 0; i < 16; i++) {
				if (sender == DataRenderer [i]) {
					int line = Int32.Parse (args.Path);
					offset = line * 16 + i;
					break;
				}
			}

			if (offset < 0)
				throw new InternalError ();

			int value;
			try {
				value = Int32.Parse (args.NewText, NumberStyles.HexNumber);
			} catch {
				Report.Error ("Invalid number!");
				return;
			}

			if ((value < 0) || (value >= 256)) {
				Report.Error ("Value must be between 0 and 255!");
				return;
			}

			try {
				backend.TargetMemoryAccess.WriteByte (start + offset, (byte) value);
			} catch (Exception e) {
				Report.Error ("Can't modify memory: {0}", e);
			}

			UpdateDisplay ();
		}

		void close_activated (object sender, EventArgs args)
		{
			Hide ();
		}

		void up_activated (object sender, EventArgs args)
		{
			if (current_area == null)
				return;

			if (start == current_area.Start) {
				Report.Error ("You cannot scroll up because you are already at the " +
					      "beginning of this memory area.");
				return;
			}

			start -= 16;
			if (start < current_area.Start)
				start = current_area.Start;

			TreeIter iter;
			store.Prepend (out iter);
			add_line (iter, start);

			size_entry.Text = String.Format ("0x{0:x}", end - start);

			TreePath path = store.GetPath (iter);
			tree.SetCursor (path, AddressCol, false);
			tree.ScrollToCell (path, AddressCol, false, 0.0F, 0.0F);
		}

		void down_activated (object sender, EventArgs args)
		{
			if (current_area == null)
				return;

			if (end + 1 >= current_area.End) {
				Report.Error ("You cannot scroll down because you are already at the " +
					      "end of this memory area.");
				return;
			}

			TargetAddress old_end = end;
			end += 16;
			if (end > current_area.End)
				end = current_area.End;

			size_entry.Text = String.Format ("0x{0:x}", end - start);

			if (((old_end - start) % 16) != 0) {
				UpdateDisplay ();
				return;
			}

			TreeIter iter;
			store.Append (out iter);
			add_line (iter, old_end);

			TreePath path = store.GetPath (iter);
			tree.SetCursor (path, AddressCol, false);
			tree.ScrollToCell (path, AddressCol, false, 0.0F, 0.0F);
		}

		void modules_changed ()
		{
			try {
				update_areas ();
			} catch {
				memory_maps = null;
				current_area = null;
				areas = new string [0];
			}

			UpdateDisplay ();
		}

		void update_areas ()
		{
			ArrayList list = new ArrayList ();

			memory_maps = backend.GetMemoryMaps ();
			foreach (TargetMemoryArea area in memory_maps) {
				list.Add (area.ToString ());
			}

			areas = new string [list.Count];
			list.CopyTo (areas, 0);

			area_combo.SetPopdownStrings (areas);
		}

		void area_activated (object sender, EventArgs args)
		{
			current_area = null;
			for (int i = 0; i < areas.Length; i++) {
				if (areas [i] == area_entry.Text) {
					current_area = memory_maps [i];
					break;
				}
			}

			if (current_area == null) {
				UpdateDisplay ();
				return;
			}

			start = current_area.Start;
			end = start + 0x100;
			if (end > current_area.End)
				end = current_area.End;

			address_entry.Text = String.Format ("{0:x}", start);
			size_entry.Text = String.Format ("0x{0:x}", end - start);

			UpdateDisplay ();
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
				Report.Error ("Invalid number in address field!");
				return;
			}

			try {
				string text = size_entry.Text;

				if (text == "") {
					size_entry.Text = "0x100";
					size = 0x100;
				} else if (text.StartsWith ("0x"))
					size = Int64.Parse (text.Substring (2), NumberStyles.HexNumber);
				else
					size = Int64.Parse (text);
			} catch {
				Report.Error ("Invalid number in size field!");
				return;
			}

			current_area = null;
			start = new TargetAddress (backend.Inferior, address);

			try {
				update_areas ();
				foreach (TargetMemoryArea area in memory_maps) {
					if ((area.Start > start) || (area.End <= start))
						continue;

					current_area = area;
					break;
				}
			} catch (Exception e) {
				current_area = null;
				Report.Error ("Can't get memory maps from target: {0}", e);
				return;
			}

			if (current_area == null) {
				Report.Error ("No memory area contains requested address!");
				return;
			}

			end = start + size;
			if (end > current_area.End) {
				end = current_area.End;
				size = end - start;
				size_entry.Text = String.Format ("0x{0:x}", size);
				Report.Warning ("Requested size is larger than containing memory " +
						"area ({0}-{1}), truncating to 0x{2:x}.",
						current_area.Start, current_area.End, size);
			}

			try {
				UpdateDisplay ();
			} catch (Exception e) {
				current_area = null;
				Report.Error ("Error while displaying memory maps: {0}", e);
				return;
			}
		}

		string[] areas = null;
		TargetMemoryArea[] memory_maps = null;
		TargetMemoryArea current_area = null;
		TargetAddress start = TargetAddress.Null;
		TargetAddress end = TargetAddress.Null;

		void UpdateDisplay ()
		{
			store.Clear ();

			if (current_area == null)
				return;

			for (int i = 0; i < 16; i++) {
				if (!force_writable &&
				    (current_area.Flags & TargetMemoryFlags.ReadOnly) != 0)
					DataRenderer [i].Editable = false;
				else
					DataRenderer [i].Editable = true;

				DataCol [i].Title = String.Format ("0{0:X}", (start.Address + i) % 16);
			}

			area_entry.Text = current_area.ToString ();

			for (TargetAddress ptr = start; ptr < end; ptr += 16) {
				TreeIter iter;
				store.Append (out iter);
				add_line (iter, ptr);
			}

			size_entry.Text = String.Format ("0x{0:x}", end - start);
		}

		void add_line (TreeIter iter, TargetAddress address)
		{
			char[] data = new char [16];

			int count;
			if (address + 16 <= end)
				count = 16;
			else
				count = (int) (end - address);

			for (int i = 0 ; i < count; i++) {
				string text;

				try {
					byte b = backend.TargetMemoryAccess.ReadByte (address + i);
					text = String.Format ("{1}{0:x}", b, b >= 16 ? "" : "0");
					if (b > 0x20)
						data [i] = (char) b;
					else
						data [i] = '.';
				} catch {
					text = "   ";
					data [i] = ' ';
				}

				store.SetValue (iter, i, new GLib.Value (text));
			}

			store.SetValue (iter, 16, new GLib.Value (String.Format ("{0:x}  ", address)));
			store.SetValue (iter, 18, new GLib.Value (new String (data)));
		}

		public override void SetBackend (DebuggerBackend backend)
		{
			base.SetBackend (backend);

			backend.ModulesChangedEvent += new ModulesChangedHandler (modules_changed);
		}
	}
}
