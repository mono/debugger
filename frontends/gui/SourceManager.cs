//
// SourceManager: The source code manager
//
// This takes care of loading and showing the proper source code file
//
//

using System;
using System.Text;
using System.Collections;
using GLib;
using Gtk;
using GtkSharp;
using Pango;

namespace Mono.Debugger.GUI {

	public class SourceManager : DebuggerWidget {
		Hashtable sources; 
		SourceStatusbar source_status;
		Gtk.Notebook notebook;
		DisassemblerView disassembler_view;
		bool initialized;

		//
		// State tracking
		//
		SourceView current_source = null;

		public SourceManager (DebuggerGUI gui, string notebook_glade,
				      string disassembler_glade, string source_status_glade,
				      string register_display_glade)
			: this (gui, (Gtk.Notebook) gui.GXML [notebook_glade],
				(Gtk.Container) gui.GXML [disassembler_glade],
				new SourceStatusbar (gui, source_status_glade),
				new RegisterDisplay (gui, register_display_glade))
		{ }
		
		public SourceManager (DebuggerGUI gui, Gtk.Notebook notebook,
				      Gtk.Container disassembler_container,
				      SourceStatusbar source_status,
				      RegisterDisplay register_display)
			: base (gui, notebook)
		{
			sources = new Hashtable ();
			this.gui = gui;
			this.notebook = notebook;
			this.source_status = source_status;

			disassembler_view = new DisassemblerView (this, register_display);
			disassembler_container.Add (disassembler_view.Widget);
			disassembler_view.Widget.ShowAll ();

			notebook.SwitchPage += new SwitchPageHandler (switch_page);
		}

		protected override void SetBackend (DebuggerBackend backend)
		{
			base.SetBackend (backend);

			OnProgramLoadedEvent (backend);
		}

		protected override void SetProcess (Process process)
		{
			base.SetProcess (process);

			OnProcessCreatedEvent (process);
		}

		public event MethodInvalidHandler MethodInvalidEvent;
		public event MethodChangedHandler MethodChangedEvent;
		public event StackFrameHandler FrameChangedEvent;
		public event StackFrameInvalidHandler FramesInvalidEvent;
		public event TargetExitedHandler TargetExitedEvent;
		public event StateChangedHandler StateChangedEvent;
		public event ProgramLoadedHandler ProgramLoadedEvent;
		public event ProcessCreatedHandler ProcessCreatedEvent;

		protected void OnProgramLoadedEvent (DebuggerBackend backend)
		{
			if (ProgramLoadedEvent != null)
				ProgramLoadedEvent (this, backend);
		}

		protected void OnProcessCreatedEvent (Process process)
		{
			if (ProcessCreatedEvent != null)
				ProcessCreatedEvent (this, process);
		}

		SourceList CreateSourceView (string filename, string[] contents)
		{
			StringBuilder sb = new StringBuilder ();
			for (int i = 0; i < contents.Length; i++) {
				sb.Append (contents [i]);
				sb.Append ("\n");
			}
			return CreateSourceView (filename, sb.ToString ());
		}

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
				object key = de.Key;
				SourceList view = (SourceList) de.Value as SourceList;

				if (view.TabWidget == o){
					Widget view_widget = view.Widget;
					Widget v;
					int i = 0;

					do {
						v = notebook.GetNthPage (i);
						if (view_widget.Equals (v)){
							notebook.RemovePage (i);
							sources.Remove (key);
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

		IMethod current_method = null;
		IMethodSource current_method_source = null;

		protected override void FrameChanged (StackFrame frame)
		{
			if (frame.Method != current_method) {
				current_method = frame.Method;
				if (current_method != null) {
					if (current_method.HasSource)
						current_method_source = current_method.Source;
					else
						current_method_source = null;

					MethodChanged (current_method, current_method_source);
				} else
					MethodInvalid ();
			}

			if (FrameChangedEvent != null)
				FrameChangedEvent (frame);
		}

		protected override void FramesInvalid ()
		{
			if (FramesInvalidEvent != null)
				FramesInvalidEvent ();
		}

		protected override void StateChanged (TargetState state, int arg)
		{
			if (StateChangedEvent != null)
				StateChangedEvent (state, arg);

			base.StateChanged (state, arg);
		}

		protected override void TargetExited ()
		{
			if (TargetExitedEvent != null)
				TargetExitedEvent ();

			base.TargetExited ();
		}

		void MethodInvalid ()
		{
			if ((current_source != null) && (current_source != disassembler_view))
				current_source.Active = false;
			current_source = null;

			if (MethodInvalidEvent != null)
				MethodInvalidEvent ();
		}

		string[] GetSource (IMethodSource source)
		{
			if (source.SourceBuffer == null) {
				Console.WriteLine ("Can't find source file {0}.", source.Name);
				return null;
			}

			return source.SourceBuffer.Contents;
		}

		object GetSourceKey (IMethodSource source)
		{
			if (source.IsDynamic)
				return source.SourceBuffer;
			else
				return source.SourceFile;
		}

		SourceView GetSourceView (IMethod method, IMethodSource source)
		{
			if (source == null)
				return disassembler_view;

			object source_key = GetSourceKey (source);
			SourceList view = (SourceList) sources [source_key];
			if (view != null)
				return view;

			string[] contents = GetSource (source);
			if (contents == null)
				return disassembler_view;
			
			view = CreateSourceView (source.Name, contents);
			view.Widget.ShowAll ();
					
			sources [source_key] = view;
			notebook.InsertPage (view.Widget, view.TabWidget, -1);
			notebook.SetMenuLabelText (view.Widget, source.Name);
			view.TabWidget.ButtonClicked += new EventHandler (close_tab);

			return view;
		}

		void MethodChanged (IMethod method, IMethodSource source)
		{
			MethodInvalid ();

			current_source = GetSourceView (method, source);
			current_source.Active = true;

			if (!initialized || (notebook.Page != 0)) {
				int idx = GetPageIdx (current_source.Widget);
				if (idx != -1)
					notebook.Page = idx;
			}

			disassembler_view.Active = notebook.Page == 0;

			initialized = true;
			if (MethodChangedEvent != null)
				MethodChangedEvent (method);
		}

		public SourceLocation FindLocation (string filename, int line)
		{
			return backend.FindLocation (filename, line);
		}

		public SourceView LoadFile (string filename)
		{
			string contents;
			try {
				contents = FileUtils.GetFileContents (filename);
			} catch (Exception e) {
				Console.WriteLine ("Can't load file {0}: {1}", filename, e.Message);
				return null;
			}

			SourceList view = CreateSourceView (filename, contents);
			view.Widget.ShowAll ();

			sources [filename] = view;
			notebook.InsertPage (view.Widget, view.TabWidget, -1);
			notebook.SetMenuLabelText (view.Widget, filename);
			view.TabWidget.ButtonClicked += new EventHandler (close_tab);

			return view;
                }
	}
}
