using System;
using System.Collections;
using GLib;
using Gtk;
using GtkSharp;
using Gdk;
using Mono.Debugger;

namespace Mono.Debugger.GUI
{
	public delegate void FileSelectedHandler (string filename);

	public class FileOpenDialog : FileSelection
	{
		DebuggerBackend backend;
		Button show_list_button;
		ListStore store;
		TreeView list;
		VBox list_vbox;
		bool list_visible;
		
		public FileOpenDialog (DebuggerGUI gui, string name)
			: base (name)
		{
			ShowFileops = false;
			Response += new ResponseHandler (response_event);

			gui.Manager.ProcessCreatedEvent += new ProcessCreatedHandler (program_loaded);

			show_list_button = (Button) AddButton ("_Browse source list", 1);

			// FIXME: How can I set the default height (like, for instance, 4 lines)
			//        of this list ?
			store = new ListStore ((int)TypeFundamentals.TypeString);
			list = new TreeView (store);

			list.HeadersVisible = false;

			TreeViewColumn NameCol = new TreeViewColumn ();
			CellRenderer NameRenderer = new CellRendererText ();
			NameCol.Title = "Filename";
			NameCol.PackStart (NameRenderer, true);
			NameCol.AddAttribute (NameRenderer, "text", 0);
			list.AppendColumn (NameCol);

			list_vbox = new VBox (false, 0);

			Label label = new Label ("Source list:");
			label.Justify = Justification.Left;
			label.Xalign = 0.0F;
			list_vbox.PackStart (label, false, true, 0);

			list.RowActivated += new RowActivatedHandler (row_activated);

			ScrolledWindow sw = new ScrolledWindow ();
			sw.SetPolicy (PolicyType.Always, PolicyType.Always);
			list_vbox.PackStart (sw, true, true, 0);
			sw.Add (list);

			VBox.PackStart (list_vbox, true, true, 0);
		}

		void program_loaded (object sender, Process process)
		{
			this.backend = process.DebuggerBackend;
			UpdateList ();
		}

		ArrayList sources;

		void response_event (object sender, ResponseArgs args)
		{
			if (args.ResponseId == 1) {
				ShowList ();
				return;
			}

			Hide ();

			if (args.ResponseId == (int) ResponseType.Ok)
				OnFileSelectedEvent (Filename);
		}

		void row_activated (object sender, RowActivatedArgs args)
		{
			int index = Int32.Parse (args.Path.ToString ()); // FIXME
			Hide ();
			SourceFile source = (SourceFile) sources [index];
			OnFileSelectedEvent (source.FileName);
		}

		protected void UpdateList ()
		{
			sources = new ArrayList ();
			store.Clear ();

			if (backend == null)
				return;

			foreach (Module module in backend.Modules) {
				if (!module.SymbolsLoaded)
					continue;

				sources.AddRange (module.Sources);
			}

			foreach (SourceFile source in sources) {
				TreeIter iter;
				store.Append (out iter);
				store.SetValue (iter, 0, new GLib.Value (source.FileName));
			}
		}

		protected void ShowList ()
		{
			if (list_visible) {
				show_list_button.Label = "_Browse source list";
				list_vbox.Hide ();
			} else {
				show_list_button.Label = "_Hide source list";
				UpdateList ();
				list_vbox.ShowAll ();
			}
			list_visible = !list_visible;
		}

		public event FileSelectedHandler FileSelectedEvent;

		protected virtual void OnFileSelectedEvent (string filename)
		{
			if (FileSelectedEvent != null)
				FileSelectedEvent (filename);
		}
	}
}
