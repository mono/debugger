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

namespace Mono.Debugger.GUI {

	public class SourceList {
		ScrolledWindow sw;
		DebuggerBackend backend;
		Gtk.SourceView source_view;
		TextTag frame_tag;
		TextBuffer text_buffer;
		ClosableNotebookTab tab;
		SourceFileFactory factory;
		bool active;

		public SourceList (ISourceBuffer source_buffer, string filename)
		{
			tab = new ClosableNotebookTab (filename);

			sw = new ScrolledWindow (null, null);
			sw.SetPolicy (PolicyType.Automatic, PolicyType.Automatic);
			source_view = new Gtk.SourceView ();
			source_view.Editable = false;
			
			sw.Add (source_view);
			sw.ShowAll ();

			factory = new SourceFileFactory ();
			
			text_buffer = source_view.Buffer;
			string contents = GetSource (source_buffer);
			frame_tag = new Gtk.TextTag ("frame");
			frame_tag.Background = "red";
			text_buffer.CreateMark ("frame", text_buffer.StartIter, true);
			text_buffer.TagTable.Add (frame_tag);
			text_buffer.Insert (text_buffer.EndIter, contents, contents.Length);
		}

		string GetSource (ISourceBuffer buffer)
		{
			if (buffer.HasContents)
				return buffer.Contents;

			if (factory == null) {
				Console.WriteLine (
					"I don't have a SourceFileFactory, can't lookup source code.");
				return null;
			}

			SourceFile file = factory.FindFile (buffer.Name);
			if (file == null) {
				Console.WriteLine ("Can't find source file {0}.", buffer.Name);
				return null;
			}

			return file.Contents;
		}

		public void SetBackend (DebuggerBackend backend)
		{
			this.backend = backend;

			backend.FrameChangedEvent += new StackFrameHandler (FrameChangedEvent);
			backend.MethodInvalidEvent += new MethodInvalidHandler (MethodInvalidEvent);
		}

		public bool Active {
			get {
				return active;
			}

			set {
				active = value;
				if (!active)
					text_buffer.RemoveTag (
						frame_tag, text_buffer.StartIter, text_buffer.EndIter);
				else
					FrameChangedEvent (backend.CurrentFrame);
			}
		}

		void MethodInvalidEvent ()
		{
			Active = false;
		}

		void FrameChangedEvent (StackFrame frame)
		{
			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);

			if (!active)
				return;

			SourceLocation source = frame.SourceLocation;
			if (source == null)
				return;

			Gtk.TextIter start_iter, end_iter;
			text_buffer.GetIterAtLineOffset (out start_iter, source.Row - 1, 0);
			text_buffer.GetIterAtLineOffset (out end_iter, source.Row, 0);

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);
			text_buffer.ApplyTag (frame_tag, start_iter, end_iter);

			Gtk.TextMark frame_mark = text_buffer.GetMark ("frame");
			text_buffer.MoveMark (frame_mark, start_iter);
			source_view.ScrollToMark (frame_mark, 0.0, true, 0.0, 0.5);
		}
		
		public ClosableNotebookTab TabWidget {
			get {
				return tab;
			}
		}

		public Widget ToplevelWidget {
			get {
				return sw;
			}
		}
	}
	
	public class SourceManager {
		Hashtable sources; 
		DebuggerBackend backend;
		SourceStatusbar source_status;
		Gtk.Notebook notebook;
		bool initialized;

		//
		// State tracking
		//
		IMethod current_method = null;
		IMethodSource current_method_source = null;
		SourceList current_source = null;
		
		public SourceManager (Gtk.Notebook notebook, SourceStatusbar source_status)
		{
			sources = new Hashtable ();
			this.notebook = notebook;
			this.source_status = source_status;

			notebook.SwitchPage += new SwitchPageHandler (switch_page);
		}
		
		public void SetBackend (DebuggerBackend backend)
		{
			this.backend = backend;

			backend.MethodChangedEvent += new MethodChangedHandler (MethodChangedEvent);
			backend.MethodInvalidEvent += new MethodInvalidHandler (MethodInvalidEvent);

			foreach (DictionaryEntry de in sources){
				SourceList source = (SourceList) de.Value;

				source.SetBackend (backend);
			}
		}

		SourceList CreateSourceView (ISourceBuffer source_buffer, string filename)
		{
			return new SourceList (source_buffer, filename);
		}

		IMethodSource GetMethodSource (IMethod method)
		{
			if ((method == null) || !method.HasSource)
				return null;

			return method.Source;
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

		void MethodInvalidEvent ()
		{
			current_method = null;
			current_method_source = null;

			if (current_source != null)
				current_source.Active = false;
			current_source = null;
		}
		
		void MethodChangedEvent (IMethod method)
		{
			MethodInvalidEvent ();
			current_method = method;
			current_method_source = GetMethodSource (method);

			if (method.HasSource){
				ISourceBuffer source_buffer = method.Source.SourceBuffer;

				string filename = source_buffer.Name;

				SourceList view = (SourceList) sources [filename];
			
				if (view == null){
					view = CreateSourceView (source_buffer, filename);
					if (backend != null)
						view.SetBackend (backend);
					
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

			initialized = true;
		}
	}
}
