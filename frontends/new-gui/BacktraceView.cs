using GLib;
using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.GUI
{
	public class BacktraceView
	{
		GUIContext context;
		ProcessContainer container;
		TreeView tree;
		ListStore store;

		public BacktraceView (ProcessContainer container)
		{
			this.container = container;
			this.context = container.Context;

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

			container.Process.TargetEvent += new TargetEventHandler (OnTargetEvent);
		}

		public Widget Widget {
			get { return tree; }
		}

		protected void OnTargetEvent (object sender, TargetEventArgs args)
		{
			context.Lock ();
			store.Clear ();
			context.UnLock ();

			if (!args.IsStopped)
				return;

			Backtrace bt = container.Process.GetBacktrace ();
			if (bt == null)
				return;

			context.Lock ();

			for (int i = 0; i < bt.Length; i++)
				add_frame (i, bt [i]);

			context.UnLock ();
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
