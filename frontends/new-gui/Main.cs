using System;
using Gtk;
using GtkSharp;
using Gnome;
using Mono.Debugger;

namespace Mono.Debugger.GUI
{
	public class GUI
	{
		Program program;
		Glade.XML gxml;
		App app;
		Paned paned;
		Notebook notebook;

		GUIContext context;

		static GUI ()
		{
			GtkSharp.Mono.Debugger.GUI.ObjectManager.Initialize ();

			GLib.Log.SetAlwaysFatal (GLib.LogLevelFlags.Warning |
						 GLib.LogLevelFlags.Critical);
		}

		public GUI ()
		{
			program = new Program ("Debugger", "0.4", Modules.UI, new string [0]);

			gxml = new Glade.XML (null, "debugger.glade", null, null);

			app = (App) gxml ["debugger-toplevel"];
			paned = (Paned) gxml ["main-paned"];

			notebook = new Notebook ();
			notebook.EnablePopup = false;
			notebook.ShowTabs = false;
			paned.Add2 (notebook);

			string[] args = new string [] { "./test/MultiThread.exe" };
			CreateContext (args);

			gxml.Autoconnect (this);
			app.DeleteEvent += new DeleteEventHandler (OnWindowDelete);

			app.ShowAll ();
		}

		protected void CreateContext (string [] args)
		{
			context = new GUIContext (args);
			notebook.AppendPage (context.Widget, new Label (""));
			context.Widget.ShowAll ();
		}

		protected void DeleteWindow ()
		{
			notebook.RemovePage (0);
		}

		protected void OnFileQuitActivate (object ojb, EventArgs args)
		{
			program.Quit ();
		}

		protected void OnWindowDelete (object obj, DeleteEventArgs args)
		{
			program.Quit();
			args.RetVal = true;
		}

		public static void Run ()
		{
			Gtk.Application.Run ();
		}

		public static void Main ()
		{
			GUI gui = new GUI ();

			Run ();
		}
	}
}
