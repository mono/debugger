using GLib;
using Gtk;
using Gdk;
using GtkSharp;
using Gnome;
using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Threading;
using System.Reflection;

using Mono.Debugger;
using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.GUI;

namespace Mono.Debugger.GUI {

	public class SimpleViewer
	{
		Gtk.Statusbar status_bar;
		Gtk.TextView text_view;
		Gtk.TextView output_area;
		Gtk.TextBuffer text_buffer;
		Gtk.TextBuffer output_buffer;
		Gtk.Entry command_entry;
		Gtk.TextTag frame_tag;
		Gtk.TextMark frame_mark;

		Program kit;
		GDB backend;

		SimpleViewer (string application, string[] arguments)
		{
			string[] args = new string [0];
			kit = new Program ("simple-viewer", "0.0.1", Modules.UI, args);
			
			Gtk.Window win = CreateWindow ();
			win.ShowAll ();

			backend = new GDB (application, arguments);

			backend.TargetOutput += new TargetOutputHandler (TargetOutput);
			backend.TargetError += new TargetOutputHandler (TargetError);
			backend.StateChanged += new StateChangedHandler (StateChanged);
			backend.FrameEvent += new StackFrameHandler (FrameEvent);

			backend.SourceFileFactory = new SourceFileFactory ();
		}

		StringBuilder output_builder = null;

		void TargetOutput (string output)
		{
			if (output_builder != null)
				output_builder.Append (output);
			else
				AddOutput (output);
		}

		void TargetError (string output)
		{
			AddOutput (output);
		}

		public void AddOutput (string output)
		{
			output_buffer.Insert (output_buffer.EndIter, output + "\n",
					      output.Length+1);
			output_area.ScrollToMark (output_buffer.InsertMark, 0.4, true, 0.0, 1.0);
		}

		void StateChanged (TargetState new_state)
		{
			if (new_state == TargetState.RUNNING)
				text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);

			Console.WriteLine ("STATE CHANGED: " + new_state);
		}

		ISourceFile current_file = null;

		void FrameEvent (IStackFrame frame)
		{
			if ((frame.SourceFile == null) || (frame.SourceFile.FileContents == null))
				return;

			Gtk.TextBuffer buffer = text_view.Buffer;

			if (current_file != frame.SourceFile) {
				current_file = frame.SourceFile;

				text_buffer.Delete (text_buffer.StartIter, text_buffer.EndIter);

				text_buffer.Insert (text_buffer.EndIter, frame.SourceFile.FileContents,
						    frame.SourceFile.FileContents.Length);
			}

			Gtk.TextIter start_iter, end_iter;
			text_buffer.GetIterAtLineOffset (out start_iter, frame.Row - 1, 0);
			text_buffer.GetIterAtLineOffset (out end_iter, frame.Row, 0);

			text_buffer.RemoveTag (frame_tag, text_buffer.StartIter, text_buffer.EndIter);
			text_buffer.ApplyTag (frame_tag, start_iter, end_iter);

			text_buffer.MoveMark (frame_mark, start_iter);

			text_view.ScrollToMark (frame_mark, 0.0, true, 0.0, 0.5);

			string text = frame.SourceFile.FileInfo.Name + " line " + frame.Row;

			uint id = status_bar.GetContextId ("frame");
			status_bar.Push (id, text);
		}

		Gtk.TextView CreateSourceView ()
		{
			Gtk.TextView text = new Gtk.TextView ();

			text.Editable = false;

			frame_tag = new Gtk.TextTag ("frame");
			frame_tag.Background = "red";

			text.Buffer.TagTable.Add (frame_tag);

			frame_mark = text.Buffer.CreateMark ("frame", text.Buffer.StartIter, true);

			return text;
		}

		Gtk.TextView CreateOutputArea ()
		{
			Gtk.TextView text = new Gtk.TextView ();

			text.Editable = true;
			text.WrapMode = Gtk.WrapMode.None;

			return text;
		}

		Gtk.Statusbar CreateStatusBar ()
		{
			Gtk.Statusbar status = new Gtk.Statusbar ();

			status.HasResizeGrip = false;

			return status;
		}

		Gtk.Entry CreateCommandEntry ()
		{
			Gtk.Entry entry = new Gtk.Entry ();

			entry.ActivatesDefault = true;
			entry.Activated += new EventHandler (DoOneCommand);

			return entry;
		}

		void ShowHelp ()
		{
			AddOutput ("Commands:");
			AddOutput ("  q, quit,exit     Quit the debugger");
			AddOutput ("  r, run           Start the target");
			AddOutput ("  c, continue      Continue the target");
			AddOutput ("  abort            Abort the target");
			AddOutput ("  kill             Kill the target");
			AddOutput ("  b, break-method  Add breakpoint for a CSharp method");
			AddOutput ("  f, frame         Get current stack frame");
			AddOutput ("  s, step          Single-step");
			AddOutput ("  n, next          Single-step");
			AddOutput ("  !, gdb           Send command to gdb");
		}

		void DoOneCommand (object sender, EventArgs event_args)
		{
			string line = command_entry.Text;
			command_entry.Text = "";

			if (line == "")
				return;

			string[] tmp_args = line.Split (' ', '\t');
			string[] args = new string [tmp_args.Length - 1];
			Array.Copy (tmp_args, 1, args, 0, tmp_args.Length - 1);
			string command = tmp_args [0];

			switch (command) {
			case "h":
			case "help":
				ShowHelp ();
				break;

			case "q":
			case "quit":
			case "exit":
				backend.Quit ();
				Application.Quit ();
				break;

			case "r":
			case "run":
				backend.Run ();
				break;

			case "c":
			case "continue":
				backend.Continue ();
				break;

			case "f":
			case "frame":
				backend.Frame ();
				break;

			case "s":
			case "step":
				backend.Step ();
				break;

			case "n":
			case "next":
				backend.Next ();
				break;

			case "abort":
				backend.Abort ();
				break;

			case "kill":
				backend.Kill ();
				break;

			case "sleep":
				Thread.Sleep (50000);
				break;

			case "b":
			case "break-method": {
				if (args.Length != 2) {
					AddOutput ("Command requires an argument");
					break;
				}

				ILanguageCSharp csharp = backend as ILanguageCSharp;
				if (csharp == null) {
					AddOutput ("Debugger doesn't support C#");
					break;
				}

				Type type = csharp.CurrentAssembly.GetType (args [0]);
				if (type == null) {
					AddOutput ("No such type: `" + args [0] + "'");
					break;
				}

				MethodInfo method = type.GetMethod (args [1]);
				if (method == null) {
					AddOutput ("Can't find method `" + args [1] + "' in type `" +
						   args [0] + "'");
					break;
				}

				ITargetLocation location = csharp.CreateLocation (method);
				if (location == null) {
					AddOutput ("Can't get location for method: " +
						   args [0] + "." + args [1]);
					break;
				}

				IBreakPoint break_point = backend.AddBreakPoint (location);

				if (break_point != null)
					AddOutput ("Added breakpoint: " + break_point);
				else
					AddOutput ("Unable to add breakpoint!");

				break;
			}

			case "!":
			case "gdb": {
				GDB gdb = backend as GDB;
				if (gdb == null) {
					AddOutput ("This command is only available when using " +
						   "gdb as backend");
					break;
				}

				output_builder = new StringBuilder ();
				gdb.SendUserCommand (String.Join (" ", args));
				AddOutput (output_builder.ToString ());
				output_builder = null;
				break;
			}

			default:
				AddOutput ("Unknown command: " + command);
				break;
			}
		}

		public Gtk.Window CreateWindow ()
		{
			Gnome.App win = new Gnome.App ("simple-viewer", "Mono Debugger");
			win.DeleteEvent += new DeleteEventHandler (Window_Delete);

			text_view = CreateSourceView ();
			output_area = CreateOutputArea ();
			status_bar = CreateStatusBar ();
			command_entry = CreateCommandEntry ();

			output_buffer = output_area.Buffer;
			text_buffer = text_view.Buffer;

			VBox vbox = new VBox (false, 0);

			Gtk.Frame command_frame = new Gtk.Frame ("Command");
			command_frame.Add (command_entry);

			Gtk.Frame source_frame = new Gtk.Frame ("Source code");
			Gtk.ScrolledWindow source_sw = new Gtk.ScrolledWindow ();
			source_frame.Add (source_sw);
			source_sw.Add (text_view);

			Gtk.Frame output_frame = new Gtk.Frame ("Output");
			Gtk.ScrolledWindow output_sw = new Gtk.ScrolledWindow ();
			output_sw.VscrollbarPolicy = Gtk.PolicyType.Always;
			output_sw.HscrollbarPolicy = Gtk.PolicyType.Always;
			output_frame.Add (output_sw);
			output_sw.Add (output_area);

			vbox.PackStart (command_frame, false, true, 4);
			vbox.PackStart (source_frame, true, true, 4);
			vbox.PackStart (output_frame, true, true, 4);
			vbox.PackStart (status_bar, false, true, 4);

			win.Contents = vbox;

			win.DefaultSize = new Size (800, 500);

			return win;
		}

		public void Run ()
		{
			kit.Run ();
		}
	
		static void Window_Delete (object obj, DeleteEventArgs args)
		{
			SignalArgs sa = (SignalArgs) args;
			Application.Quit ();
			sa.RetVal = true;
		}

		//
		// Main
		//
		static void Main (string[] args)
		{
			if (args.Length < 1) {
				Console.WriteLine ("Usage: {0} application.exe [args]",
						   AppDomain.CurrentDomain.FriendlyName);
				Environment.Exit (1);
			}

			string[] new_args = new string [args.Length - 1];
			Array.Copy (args, 1, new_args, 0, args.Length - 1);

			SimpleViewer viewer = new SimpleViewer (args [0], new_args);

			viewer.Run ();
		}
	}
}
