using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Collections;

using Mono.Debugger.Languages;

namespace Mono.Debugger.Backends
{
	public class GDB : IDebuggerBackend, ILanguageCSharp, IDisposable
	{
		public const string Path_GDB	= "/usr/bin/gdb";
		public const string Path_Mono	= "/home/martin/MONO-LINUX/bin/mono";

		Process process;
		StreamWriter gdb_pipe;
		Stream gdb_output;
		Stream gdb_errors;

		Thread gdb_output_thread;
		Thread gdb_error_thread;

		Hashtable symbols;
		Hashtable breakpoints;

		Assembly application;

		ISourceFileFactory source_file_factory;

		ManualResetEvent gdb_event;

		int last_breakpoint_id;

		public GDB (string application, string[] arguments)
			: this (Path_GDB, Path_Mono, application, arguments)
		{ }

		public GDB (string gdb_path, string mono_path, string application, string[] arguments)
		{
			this.application = Assembly.LoadFrom (application);

			MethodInfo main = this.application.EntryPoint;
			string main_name = main.DeclaringType + ":" + main.Name;

			string args = "-n -nw -q -x mono-debugger.gdbinit --annotate=2 --async " +
				"--args " + mono_path + " --break " + main_name +  " --debug=dwarf " +
				"--noinline --precompile @" + application + " " + application + " " +
				String.Join (" ", arguments);

			ProcessStartInfo start_info = new ProcessStartInfo (gdb_path, args);

			start_info.RedirectStandardInput = true;
			start_info.RedirectStandardOutput = true;
			start_info.RedirectStandardError = true;
			start_info.UseShellExecute = false;

			process = Process.Start (start_info);
			gdb_pipe = process.StandardInput;
			gdb_pipe.AutoFlush = true;

			gdb_event = new ManualResetEvent (false);

			gdb_output = process.StandardOutput.BaseStream;
			gdb_errors = process.StandardError.BaseStream;

			gdb_output_thread = new Thread (new ThreadStart (check_gdb_output));
			gdb_output_thread.Start ();

			gdb_error_thread = new Thread (new ThreadStart (check_gdb_errors));
			gdb_error_thread.Start ();

			send_gdb_command ("run");
			send_gdb_command ("call mono_debug_make_symbols ()");
			send_gdb_command ("add-symbol-file " + application + ".o");

			symbols = new Hashtable ();
			breakpoints = new Hashtable ();
		}

		//
		// IDebuggerBackend
		//

		TargetState target_state = TargetState.NO_TARGET;
		public TargetState State {
			get {
				return target_state;
			}
		}

		public void Run ()
		{
			gdb_pipe.WriteLine ("run");
		}

		public void Continue ()
		{
			gdb_pipe.WriteLine ("continue");
		}

		public void Quit ()
		{
			gdb_pipe.WriteLine ("quit");
		}

		public void Abort ()
		{
			gdb_pipe.WriteLine ("signal SIGTERM");
		}

		public void Kill ()
		{
			gdb_pipe.WriteLine ("kill");
		}

		public void Frame ()
		{
			gdb_pipe.WriteLine ("frame");
		}

		public void Step ()
		{
			gdb_pipe.WriteLine ("step");
		}

		public void Next ()
		{
			gdb_pipe.WriteLine ("next");
			gdb_pipe.WriteLine ("frame");
		}

		public IBreakPoint AddBreakPoint (ITargetLocation location)
		{
			long address = location.Location;
			if (address == -1)
				return null;

			last_breakpoint_id = -1;

			wait_for = WaitForOutput.ADD_BREAKPOINT;
			send_gdb_command ("break *" + address);

			if (last_breakpoint_id == -1)
				return null;

			BreakPoint breakpoint = new BreakPoint (this, location, last_breakpoint_id);
			breakpoints.Add (last_breakpoint_id, breakpoint);
			return (IBreakPoint) breakpoint;
		}

		TargetOutputHandler target_output = null;
		TargetOutputHandler target_error = null;
		StateChangedHandler state_changed = null;
		StackFrameHandler frame_event = null;

		public event TargetOutputHandler TargetOutput {
			add {
				target_output += value;
			}

			remove {
				target_output -= value;
			}
		}

		public event TargetOutputHandler TargetError {
			add {
				target_error += value;
			}

			remove {
				target_error -= value;
			}
		}

		public event StateChangedHandler StateChanged {
			add {
				state_changed += value;
			}

			remove {
				state_changed -= value;
			}
		}

		public event StackFrameHandler FrameEvent {
			add {
				frame_event += value;
			}

			remove {
				frame_event -= value;
			}
		}

		public ISourceFileFactory SourceFileFactory {
			get {
				return source_file_factory;
			}

			set {
				source_file_factory = value;
			}
		}

		//
		// ILanguageCSharp
		//

		ITargetLocation ISourceLanguage.MainLocation {
			get {
				return ILanguageCSharp.CreateLocation (application.EntryPoint);
			}
		}

		ITargetLocation ILanguageCSharp.CreateLocation (MethodInfo method)
		{
			return new TargetAddress (this, method.DeclaringType + "." + method.Name);
		}

		Assembly ILanguageCSharp.CurrentAssembly {
			get {
				return application;
			}
		}

		//
		// private interface implementations.
		//

		protected class TargetAddress : ITargetLocation
		{
			public readonly string SymbolName;
			public readonly GDB GDB;

			public TargetAddress (GDB gdb, string symbol)
			{
				this.SymbolName = symbol;
				this.GDB = gdb;
			}

			long ITargetLocation.Location {
				get {
					if (GDB.symbols.Contains (SymbolName))
						return (long) GDB.symbols [SymbolName];

					GDB.wait_for = WaitForOutput.INFO_ADDRESS;
					GDB.send_gdb_command ("info address " + SymbolName);

					if (GDB.symbols.Contains (SymbolName))
						return (long) GDB.symbols [SymbolName];

					return -1;
				}
			}
		}

		protected class BreakPoint : IBreakPoint
		{
			public readonly GDB GDB;

			ITargetLocation address;
			int hit_count = 0;
			bool enabled = true;
			int ID;

			public BreakPoint (GDB gdb, ITargetLocation address, int id)
			{
				this.GDB = gdb;
				this.address = address;
				this.ID = id;
			}

			public void EmitHitEvent ()
			{
				++hit_count;

				if (Hit != null)
					Hit (this);
			}

			ITargetLocation IBreakPoint.TargetLocation {
				get {
					return address;
				}
			}

			int IBreakPoint.HitCount {
				get {
					return hit_count;
				}
			}

			bool IBreakPoint.Enabled {
				get {
					return enabled;
				}

				set {
					enabled = value;
					if (enabled)
						GDB.send_gdb_command ("enable " + ID);
					else
						GDB.send_gdb_command ("disable " + ID);
				}
			}

			public event BreakPointHandler Hit;
		}

		protected class StackFrame : IStackFrame
		{
			public readonly GDB GDB;

			public ISourceFile SourceFile = null;
			public int Row = 0;

			public StackFrame (GDB gdb)
			{
				this.GDB = gdb;
			}

			ISourceFile IStackFrame.SourceFile {
				get {
					return SourceFile;
				}
			}

			int IStackFrame.Row {
				get {
					return Row;
				}
			}
		}

		//
		// private.
		//

		enum WaitForOutput {
			UNKNOWN,
			INFO_ADDRESS,
			ADD_BREAKPOINT,
			BREAKPOINT,
			FRAME,
			SOURCE_FILE,
			SOURCE_LINE
		}

		WaitForOutput wait_for = WaitForOutput.UNKNOWN;

		StackFrame current_frame = null;
		string source_file = null;

		void HandleAnnotation (string annotation, string[] args)
		{
			// Console.WriteLine ("ANNOTATION: |" + annotation + "|");

			switch (annotation) {
			case "starting":
				target_state = TargetState.RUNNING;
				if (state_changed != null)
					state_changed (target_state);
				break;

			case "stopped":
				if (target_state != TargetState.RUNNING)
					break;
				target_state = TargetState.STOPPED;
				if (state_changed != null)
					state_changed (target_state);
				break;

			case "exited":
				target_state = TargetState.EXITED;
				if (state_changed != null)
					state_changed (target_state);
				break;

			case "prompt":
				wait_for = WaitForOutput.UNKNOWN;
				gdb_event.Set ();
				break;

			case "breakpoint":
				wait_for = WaitForOutput.BREAKPOINT;
				break;

			case "frame-begin":
				wait_for = WaitForOutput.FRAME;
				current_frame = new StackFrame (this);
				break;

			case "frame-source-file":
				wait_for = WaitForOutput.SOURCE_FILE;
				source_file = null;
				break;

			case "frame-source-file-end":
				if ((current_frame == null) || (source_file_factory == null) ||
				    (source_file == null))
					break;

				current_frame.SourceFile = source_file_factory.FindFile (source_file);
				break;

			case "frame-source-line":
				wait_for = WaitForOutput.SOURCE_LINE;
				break;

			case "frame-end":
				if ((current_frame != null) && (frame_event != null))
					frame_event (current_frame);
				break;

			default:
				break;
			}
		}

		bool check_info_no_symbol (string line)
		{
			if (!line.StartsWith ("No symbol\""))
				return false;
			int idx = line.IndexOf ('"', 11);
			if (idx == 0)
				return false;

			string symbol = line.Substring (11, idx-11);

			line = line.Substring (idx+1);
			if (!line.StartsWith (" in current context"))
				return false;

			symbols.Remove (symbol);

			return true;
		}

		bool check_info_symbol (string line)
		{
			if (!line.StartsWith ("Symbol \""))
				return false;
			int idx = line.IndexOf ('"', 8);
			if (idx == 0)
				return false;

			string symbol = line.Substring (8, idx-8);

			line = line.Substring (idx+1);
			if (!line.StartsWith (" is a function at address "))
				return false;

			string address = line.Substring (28, line.Length-29);

			long addr;
			try {
				addr = Int64.Parse (address, NumberStyles.HexNumber);
			} catch {
				return false;
			}

			if (symbols.Contains (symbol))
				symbols.Remove (symbol);
			symbols.Add (symbol, addr);

			return true;
		}

		bool check_add_breakpoint (string line)
		{
			if (!line.StartsWith ("Breakpoint "))
				return false;

			int idx = line.IndexOf (' ', 11);
			if (idx == 0)
				return false;
			if (!line.Substring (idx).StartsWith (" at "))
				return false;

			string id_str = line.Substring (11, idx-11);

			int id;
			try {
				id = Int32.Parse (id_str);
			} catch {
				return false;
			}

			last_breakpoint_id = id;

			return true;
		}

		bool check_breakpoint (string line)
		{
			if (!line.StartsWith ("Breakpoint "))
				return false;
			int idx = line.IndexOf (',', 11);
			if (idx < 0)
				return false;

			int id;
			try {
				id = Int32.Parse (line.Substring (11, idx-11));
			} catch {
				return false;
			}

			BreakPoint breakpoint = (BreakPoint) breakpoints [id];
			if (breakpoint != null) {
				Console.WriteLine ("HIT BREAKPOINT: " + id + " " + breakpoint);

				breakpoint.EmitHitEvent ();
			}

			return true;
		}

		bool HandleOutput (string line)
		{
			switch (wait_for) {
			case WaitForOutput.INFO_ADDRESS:
				if (check_info_symbol (line) || check_info_no_symbol (line))
					return true;
				break;
			case WaitForOutput.ADD_BREAKPOINT:
				if (check_add_breakpoint (line))
					return true;
				break;

			case WaitForOutput.BREAKPOINT:
				if (check_breakpoint (line))
					return true;
				break;

			case WaitForOutput.SOURCE_FILE:
				source_file = line;
				wait_for = WaitForOutput.FRAME;
				return true;

			case WaitForOutput.SOURCE_LINE:
				try {
					if (current_frame != null)
						current_frame.Row = Int32.Parse (line);
					return true;
				} catch {
				}
				break;

			case WaitForOutput.FRAME:
				return true;

			default:
				break;
			}

			return false;
		}

		public void SendUserCommand (string command)
		{
			send_gdb_command (command);
		}

		void send_gdb_command (string command)
		{
			Console.WriteLine ("SENDING `{0}'", command);
			gdb_event.Reset ();
			gdb_pipe.WriteLine (command);
			gdb_event.WaitOne ();
			Console.WriteLine ("DONE");
		}

		string read_one_line (Stream stream)
		{
			StringBuilder text = new StringBuilder ();

			while (true) {
				int c = stream.ReadByte ();

				if (c == -1) {				// end of stream
					if (text.Length == 0)
						return null;

					break;
				}

				if (c == '\n') {			// newline
					if ((text.Length > 0) && (text [text.Length - 1] == '\r'))
						text.Length--;
					break;
				}

				text.Append ((char) c);
			}

			return text.ToString ();
		}

		void check_gdb_output (bool is_stderr)
		{
			while (true) {
				string line = read_one_line (is_stderr ? gdb_errors : gdb_output);

				if (line == "")
					continue;
				if (line == null)
					break;

				if ((line.Length > 2) && (line [0] == 26) && (line [1] == 26)) {
					string annotation = line.Substring (2);
					string[] args;

					int idx = annotation.IndexOf (' ');
					if (idx > 0) {
						args = annotation.Substring (idx+1).Split (' ');
						annotation = annotation.Substring (0, idx);
					} else
						args = new string [0];

					HandleAnnotation (annotation, args);
				} else if (!HandleOutput (line)) {
					if (is_stderr) {
						if (target_error != null)
							target_error (line);
					} else {
						if (target_output != null)
							target_output (line);
					}
				}
			}
		}

		void check_gdb_output ()
		{
			check_gdb_output (false);
		}

		void check_gdb_errors ()
		{
			check_gdb_output (true);
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					// Do stuff here
					Quit ();

					gdb_output_thread.Abort ();
					gdb_error_thread.Abort ();
				}
				
				// Release unmanaged resources
				this.disposed = true;

				lock (this) {
					process.Kill ();
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~GDB ()
		{
			Dispose (false);
		}
	}
}
