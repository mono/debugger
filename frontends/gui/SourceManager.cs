//
// SourceManager: The source code manager
//
// This takes care of loading and showing the proper source code file
//
//

using System;
using System.Collections;
using Gtk;
using GtkSharp;
using Pango;

namespace Mono.Debugger.GUI {

	public class SourceManager : DebuggerWidget {
		Hashtable sources; 
		SourceStatusbar source_status;
		Gtk.Notebook notebook;
		bool initialized;

		//
		// State tracking
		//
		SourceList current_source = null;
		
		public SourceManager (DebuggerGUI gui, Gtk.Notebook notebook, SourceStatusbar source_status)
			: base (gui, notebook)
		{
			sources = new Hashtable ();
			this.gui = gui;
			this.notebook = notebook;
			this.source_status = source_status;

			notebook.SwitchPage += new SwitchPageHandler (switch_page);
		}

		public override void SetProcess (Process process)
		{
			base.SetProcess (process);

			foreach (DictionaryEntry de in sources){
				SourceList source = (SourceList) de.Value;

				source.SetProcess (process);
			}
		}

		public event MethodInvalidHandler MethodInvalidEvent;
		public event MethodChangedHandler MethodChangedEvent;
		public event StackFrameHandler FrameChangedEvent;
		public event StackFrameInvalidHandler FramesInvalidEvent;

		SourceList CreateSourceView (ISourceBuffer source_buffer, string filename)
		{
			return new SourceList (this, source_buffer, filename);
		}

		int GetPageIdx (Gtk.Widget w)
		{
			Widget v;
			int i = 0;
			
			do {
				v = notebook.GetNthPage (i);
				if (v != null){
					if (w.Equals (v))
						return i;
				}
				i++;
			} while (v != null);
			return -1;
		}
		
		void close_tab (object o, EventArgs args)
		{
			foreach (DictionaryEntry de in sources){
				string name = (string) de.Key;
				SourceList view = (SourceList) de.Value;

				if (view.TabWidget == o){
					Widget view_widget = view.ToplevelWidget;
					Widget v;
					int i = 0;

					do {
						v = notebook.GetNthPage (i);
						Console.WriteLine ("trying: {0} vs {1}", view_widget, v);
						if (view_widget.Equals (v)){
							notebook.RemovePage (i);
							sources [name] = null;
							return;
						}
						i++;
					} while (v != null);
				}
			}
		}

		void switch_page (object o, SwitchPageArgs args)
		{
			source_status.IsSourceStatusBar = args.PageNum != 0;
		}

		protected override void FrameChanged (StackFrame frame)
		{
			if (FrameChangedEvent != null)
				FrameChangedEvent (frame);
		}

		protected override void FramesInvalid ()
		{
			if (FramesInvalidEvent != null)
				FramesInvalidEvent ();
		}

		protected override void MethodInvalid ()
		{
			if (current_source != null)
				current_source.Active = false;
			current_source = null;

			if (MethodInvalidEvent != null)
				MethodInvalidEvent ();
		}
		
		protected override void MethodChanged (IMethod method, IMethodSource source)
		{
			MethodInvalid ();

			if (source != null){
				ISourceBuffer source_buffer = source.SourceBuffer;

				if (source_buffer == null) {
					current_source = null;
					goto done;
				}

				string filename = source_buffer.Name;

				SourceList view = (SourceList) sources [filename];
			
				if (view == null){
					view = CreateSourceView (source_buffer, filename);
					if (process != null)
						view.SetProcess (process);
					
					sources [filename] = view;
					notebook.InsertPage (view.ToplevelWidget, view.TabWidget, -1);
					notebook.SetMenuLabelText (view.ToplevelWidget, filename);
					view.TabWidget.ButtonClicked += new EventHandler (close_tab);
				}

				view.Active = true;

				if (!initialized || (notebook.Page != 0)) {
					int idx = GetPageIdx (view.ToplevelWidget);
					if (idx != -1)
						notebook.Page = idx;
				}

				current_source = view;
			} else {
				Console.WriteLine ("********* Need to show disassembly **********");
				current_source = null;
			}

		done:
			initialized = true;
			if (MethodChangedEvent != null)
				MethodChangedEvent (method);
		}
	}
}
