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
		DisassemblerView disassembler_view;
		SourceFileFactory factory;
		bool initialized;

		//
		// State tracking
		//
		SourceView current_source = null;
		
		public SourceManager (DebuggerGUI gui, Gtk.Notebook notebook,
				      Gtk.Container disassembler_container,
				      SourceStatusbar source_status)
			: base (gui, notebook)
		{
			sources = new Hashtable ();
			this.gui = gui;
			this.notebook = notebook;
			this.source_status = source_status;

			disassembler_view = new DisassemblerView (this, disassembler_container);

			factory = new SourceFileFactory ();

			notebook.SwitchPage += new SwitchPageHandler (switch_page);
		}

		public override void SetProcess (Process process)
		{
			disassembler_view.SetProcess (process);
			disassembler_view.Active = true;

			base.SetProcess (process);
		}

		public event MethodInvalidHandler MethodInvalidEvent;
		public event MethodChangedHandler MethodChangedEvent;
		public event StackFrameHandler FrameChangedEvent;
		public event StackFrameInvalidHandler FramesInvalidEvent;

		SourceList CreateSourceView (string filename, string contents)
		{
			return new SourceList (this, filename, contents);
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
			disassembler_view.Active = args.PageNum == 0;
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

		protected string GetSource (ISourceBuffer buffer)
		{
			if (buffer.HasContents)
				return buffer.Contents;

			SourceFile file = factory.FindFile (buffer.Name);
			if (file == null) {
				Console.WriteLine ("Can't find source file {0}.", buffer.Name);
				return null;
			}

			return file.Contents;
		}

		protected SourceView GetSourceView (IMethod method, IMethodSource source)
		{
			if (source == null)
				return disassembler_view;

			ISourceBuffer source_buffer = source.SourceBuffer;
			if (source_buffer == null)
				return disassembler_view;

			string filename = source_buffer.Name;
			SourceList view = (SourceList) sources [filename];

			if (view != null)
				return view;

			string contents = GetSource (source_buffer);
			if (contents == null)
				return disassembler_view;
			
			view = CreateSourceView (filename, contents);
			view.SetProcess (process);
					
			sources [filename] = view;
			notebook.InsertPage (view.ToplevelWidget, view.TabWidget, -1);
			notebook.SetMenuLabelText (view.ToplevelWidget, filename);
			view.TabWidget.ButtonClicked += new EventHandler (close_tab);

			return view;
		}

		protected override void RealMethodChanged (IMethod method)
		{
			base.RealMethodChanged (method);

			disassembler_view.RealMethodChanged (method);
		}

		protected override void RealMethodInvalid ()
		{
			base.RealMethodInvalid ();

			disassembler_view.RealMethodInvalid ();
		}

		protected override void MethodChanged (IMethod method, IMethodSource source)
		{
			MethodInvalid ();

			current_source = GetSourceView (method, source);
			current_source.Active = true;

			if (!initialized || (notebook.Page != 0)) {
				int idx = GetPageIdx (current_source.ToplevelWidget);
				if (idx != -1)
					notebook.Page = idx;
			}

			disassembler_view.Active = notebook.Page == 0;

		done:
			initialized = true;
			if (MethodChangedEvent != null)
				MethodChangedEvent (method);
		}
	}
}
