using System;
using System.Collections;
using Gtk;
using GtkSharp;
using Gnome;
using System.Threading;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Frontends.CommandLine;

namespace Mono.Debugger.GUI
{
	public class GUIContext
	{
		Notebook notebook;
		Hashtable page_hash;

		ProcessContainer current_process;

		VBox vbox;
		Gtk.Entry entry;
		OutputWindow output;
		ScriptingContext context;
		Interpreter interpreter;
		Mutex mutex;

		string command;
		ThreadNotify thread_notify;
		int notify_id;

		DebuggerBackend backend;

		public GUIContext (string[] args)
		{
			notebook = new Notebook ();
			page_hash = new Hashtable ();
			notebook.SwitchPage += new SwitchPageHandler (switch_page);

			vbox = new VBox (false, 16);
			notebook.AppendPage (vbox, new Label ("Command"));

			entry = new Gtk.Entry ();
			entry.ActivatesDefault = true;
			vbox.PackStart (entry, false, true, 4);

			output = new OutputWindow (this);
			vbox.PackStart (output.Widget);

			context = new ScriptingContext (output, output, false, true, args);
			interpreter = new Interpreter (context);

			mutex = new Mutex ();
			thread_notify = new ThreadNotify ();
			notify_id = thread_notify.RegisterListener (new ReadyEventHandler (command_event));

			entry.Activated += new EventHandler (entry_activated);

			backend = context.Initialize ();

			backend.ThreadManager.ThreadCreatedEvent += new ThreadEventHandler (thread_created);
			backend.ThreadManager.MainThreadCreatedEvent += new ThreadEventHandler (main_thread_created);

			context.Run ();
		}

		public Widget Widget {
			get { return notebook; }
		}

                [DllImport("libgdk-win32-2.0-0.dll")]
                static extern void gdk_flush ();

                [DllImport("libgdk-win32-2.0-0.dll")]
                static extern void gdk_threads_init ();

		static GUIContext ()
		{
			gdk_threads_init ();
		}

		int locked = 0;

		// <summary>
		//   Acquire the GDK lock.
		// </summary>
		// <remarks>
		//   This function may be called recursively.
		// </remarks>
		public void Lock ()
		{
			mutex.WaitOne ();
			lock (this) {
				if (locked++ == 0)
					Gdk.Threads.Enter ();
			}
		}

		// <summary>
		//   Release the GDK lock.
		// </summary>
		// <remarks>
		//   This function may be called recursively.
		// </remarks>
		public void UnLock ()
		{
			lock (this) {
				if (--locked == 0) {
					gdk_flush ();
					Gdk.Threads.Leave ();
				}
			}
			mutex.ReleaseMutex ();
		}

		// <summary>
		//   Add process @process to the GUI.
		// </summary>
		public ProcessContainer AddProcess (Process process, string title)
		{
			ProcessContainer container = new ProcessContainer (this, process);
			notebook.AppendPage (container.Widget, new Label (title));
			page_hash.Add (container.Widget, container);
			container.Widget.ShowAll ();
			return container;
		}

		void switch_page (object sender, SwitchPageArgs args)
		{
			Widget page = notebook.GetNthPage ((int) args.PageNum);
			current_process = (ProcessContainer) page_hash [page];
		}

		// <remarks>
		//   Must be run while *not* holding the GDK lock.
		// </remarks>
		void command_event ()
		{
			string current;
			lock (this) {
				current = command; command = null;
			}

			output.WriteLine (false, "$ " + current);

			try {
				interpreter.ProcessCommand (current);
			} catch (Exception ex) {
				output.WriteLine (true, ex.Message);
			}

			Lock ();
			entry.Text = "";
			entry.Sensitive = true;
			entry.HasFocus = true;
			UnLock ();
		}

		// <remarks>
		//   Called from a Gtk+ signal handler, so we're holding the Gdk lock.
		//   We cannot process the command here to avoid a deadlock.
		// </remarks>
		void entry_activated (object sender, EventArgs args)
		{
			lock (this) {
				command = entry.Text;
				entry.Sensitive = false;
				gdk_flush ();
				thread_notify.Signal (notify_id);
			}
		}

		void main_thread_created (ThreadManager manager, Process process)
		{
			Lock ();
			AddProcess (process, "Main");
			UnLock ();
		}

		void thread_created (ThreadManager manager, Process process)
		{
			Lock ();
			if (!process.IsDaemon)
				AddProcess (process, String.Format ("Process {0}", process.ID));
			UnLock ();
		}
	}
}
