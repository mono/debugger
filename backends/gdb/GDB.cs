using GLib;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Collections;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.CSharp.Debugger;

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
		Mutex gdb_mutex;

		IArchitecture arch;

		bool continue_stepping = false;

		ArrayList symtabs;

		int last_breakpoint_id;

		readonly uint target_address_size;
		readonly uint target_integer_size;
		readonly uint target_long_integer_size;

		public GDB (string application, string[] arguments)
			: this (Path_GDB, Path_Mono, application, arguments,
				new SourceFileFactory ())
		{ }

		public GDB (string gdb_path, string mono_path, string application,
			    string[] arguments, ISourceFileFactory source_factory)
		{
			this.source_file_factory = source_factory;
			this.application = Assembly.LoadFrom (application);

			MethodInfo main = this.application.EntryPoint;
			string main_name = main.DeclaringType + ":" + main.Name;

			string args = "-n -nw -q --annotate=2 --async " +
				"--args " + mono_path + " --break " + main_name +  " --debug=mono " +
				"--noinline --precompile @" + application + " " + application + " " +
				String.Join (" ", arguments);

			ProcessStartInfo start_info = new ProcessStartInfo (gdb_path, args);

			start_info.RedirectStandardInput = true;
			start_info.RedirectStandardOutput = true;
			start_info.RedirectStandardError = true;
			start_info.UseShellExecute = false;

			gdb_event = new ManualResetEvent (false);
			gdb_mutex = new Mutex (false);

			process = Process.Start (start_info);
			gdb_pipe = process.StandardInput;
			gdb_pipe.AutoFlush = true;

			gdb_output = process.StandardOutput.BaseStream;
			gdb_errors = process.StandardError.BaseStream;

			gdb_output_thread = new Thread (new ThreadStart (check_gdb_output));
			gdb_output_thread.Start ();

			gdb_error_thread = new Thread (new ThreadStart (check_gdb_errors));
			gdb_error_thread.Start ();

			Timeout.Add (100, new TimeoutHandler (IdleLoop));

			// Wait until gdb is started.
			gdb_event.WaitOne ();
			IdleLoop ();

			target_address_size = ReadInteger ("print sizeof (void*)");
			target_integer_size = ReadInteger ("print sizeof (long)");
			target_long_integer_size = ReadInteger ("print sizeof (long long)");

			arch = new ArchitectureI386 ();

			symbols = new Hashtable ();
			breakpoints = new Hashtable ();
			symtabs = new ArrayList ();

			send_gdb_command ("set prompt");
			start_target ("run", StepMode.RUN);

			UpdateSymbolFiles ();
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
			start_target ("run", StepMode.RUN);
		}

		public void Continue ()
		{
			start_target ("continue", StepMode.RUN);
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
			start_target ("stepi", StepMode.STEP_LINE);
		}
		
		public void Next ()
		{
			start_target ("nexti", StepMode.STEP_ONE);
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
		StackFrameHandler current_frame_event = null;
		StackFramesInvalidHandler frames_invalid_event = null;

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

		public event StackFrameHandler CurrentFrameEvent {
			add {
				current_frame_event += value;
			}

			remove {
				current_frame_event -= value;
			}
		}

		public event StackFramesInvalidHandler FramesInvalidEvent {
			add {
				frames_invalid_event += value;
			}

			remove {
				frames_invalid_event -= value;
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

		uint IDebuggerBackend.TargetAddressSize {
			get {
				return target_address_size;
			}
		}

		uint IDebuggerBackend.TargetIntegerSize {
			get {
				return target_integer_size;
			}
		}

		uint IDebuggerBackend.TargetLongIntegerSize {
			get {
				return target_long_integer_size;
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
			return new TargetSymbol (this, method.DeclaringType + "." + method.Name);
		}

		Assembly ILanguageCSharp.CurrentAssembly {
			get {
				return application;
			}
		}

		//
		// private interface implementations.
		//

		protected class TargetSymbol : ITargetLocation
		{
			public readonly string SymbolName;
			public readonly GDB GDB;

			public TargetSymbol (GDB gdb, string symbol)
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

		protected class TargetLocation : ITargetLocation
		{
			public long Address = -1;

			long ITargetLocation.Location {
				get {
					return Address;
				}
			}

			public override string ToString ()
			{
				StringBuilder builder = new StringBuilder ();

				if (Address > 0) {
					builder.Append ("0x");
					builder.Append (Address.ToString ("x"));
				} else
					builder.Append ("<unknown>");

				return builder.ToString ();
			}
		}

		protected class StackFrame : IStackFrame
		{
			public ISourceLocation SourceLocation = null;
			public TargetLocation TargetLocation = new TargetLocation ();

			ISourceLocation IStackFrame.SourceLocation {
				get {
					return SourceLocation;
				}
			}

			ITargetLocation IStackFrame.TargetLocation {
				get {
					return TargetLocation;
				}
			}

			public override string ToString ()
			{
				StringBuilder builder = new StringBuilder ();

				if (SourceLocation != null)
					builder.Append (SourceLocation);
				else
					builder.Append ("<unknown>");
				builder.Append (" at ");
				builder.Append (TargetLocation);

				return builder.ToString ();
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
			FRAME_ADDRESS,
			BYTE_VALUE,
			BYTE_VALUE_2,
			INTEGER_VALUE,
			INTEGER_VALUE_2,
			SIGNED_INTEGER_VALUE,
			SIGNED_INTEGER_VALUE_2,
			HEX_VALUE,
			HEX_VALUE_2,
			LONG_VALUE,
			LONG_VALUE_2,
			STRING_VALUE,
			STRING_VALUE_2
		}

		enum StepMode {
			RUN,
			STEP_ONE,
			STEP_LINE,
			STEP_LINE_2
		}

		long step_start = 0;
		int step_range = 0;
		StepMode step_mode = StepMode.STEP_ONE;

		WaitForOutput wait_for = WaitForOutput.UNKNOWN;

		StackFrame current_frame = null;
		string source_file = null;

		byte last_byte_value;
		int last_signed_int_value;
		uint last_int_value;
		long last_long_value;
		string last_string_value;
		bool last_value_ok;

		void UpdateSymbolFiles ()
		{
			long address = ReadAddress ("call /a mono_debugger_internal_get_symbol_files ()");
			long version = ReadInteger (address);

			long start, offset, size;
			address += target_address_size;

			while (true) {
				start = ReadAddress (address);
				address += target_address_size;

				if (start == 0)
					break;

				offset = ReadAddress (address);
				address += target_address_size;

				size = ReadAddress (address);
				address += target_address_size;

				string tmpfile;
				BinaryReader reader = GetTargetMemoryReader (start, size, out tmpfile);
				MonoSymbolTableReader symreader = new MonoSymbolTableReader (reader);
				reader.Close ();
				File.Delete (tmpfile);

				symtabs.Add (new CSharpSymbolTable (symreader, source_file_factory));
			}
		}

		public byte ReadByte (long address)
		{
			return ReadByte ("print/u *(char*)" + address);
		}

		byte ReadByte (string address)
		{
			last_value_ok = false;
			wait_for = WaitForOutput.BYTE_VALUE;
			send_gdb_command (address);
			if (!last_value_ok)
				throw new TargetException ("Can't read target memory at address " + address);

			return last_byte_value;
		}

		public uint ReadInteger (long address)
		{
			return ReadInteger ("print/u *(long*)" + address);
		}

		uint ReadInteger (string address)
		{
			last_value_ok = false;
			wait_for = WaitForOutput.INTEGER_VALUE;
			send_gdb_command (address);
			if (!last_value_ok)
				throw new TargetException ("Can't read target memory at address " + address);

			return last_int_value;
		}

		public int ReadSignedInteger (long address)
		{
			return ReadSignedInteger ("print/d *(long*)" + address);
		}

		int ReadSignedInteger (string address)
		{
			last_value_ok = false;
			wait_for = WaitForOutput.SIGNED_INTEGER_VALUE;
			send_gdb_command (address);
			if (!last_value_ok)
				throw new TargetException ("Can't read target memory at address " + address);

			return last_signed_int_value;
		}

		public long ReadAddress (long address)
		{
			return ReadAddress ("print/a *(void**)" + address);
		}

		long ReadAddress (string address)
		{
			last_value_ok = false;
			wait_for = WaitForOutput.HEX_VALUE;
			send_gdb_command (address);
			if (!last_value_ok)
				throw new TargetException ("Can't read target memory at address `" +
							   address + "'");

			return last_long_value;
		}

		public long ReadLongInteger (long address)
		{
			return ReadLongInteger ("print/u *(long long*)" + address);
		}

		long ReadLongInteger (string address)
		{
			last_value_ok = false;
			wait_for = WaitForOutput.LONG_VALUE;
			send_gdb_command (address);
			if (!last_value_ok)
				throw new TargetException ("Can't read target memory at address " + address);

			return last_long_value;
		}

		public string ReadString (long address)
		{
			return ReadString ("print/s *" + address);
		}

		string ReadString (string address)
		{
			last_value_ok = false;
			wait_for = WaitForOutput.STRING_VALUE;
			send_gdb_command (address);
			if (!last_value_ok)
				throw new TargetException ("Can't read target memory at address " + address);

			return last_string_value;
		}

		BinaryReader GetTargetMemoryReader (long address, long size, out string tmpfile)
		{
			tmpfile = Path.GetTempFileName ();

			send_gdb_command ("dump binary memory " + tmpfile + " " + address + " " +
					  (address + size));

			FileStream stream = new FileStream (tmpfile, FileMode.Open);
			return new BinaryReader (stream);
		}

		void frame_changed (StackFrame frame)
		{
			bool send_event = false;

			foreach (ISymbolTable symtab in symtabs) {
				ISourceLocation source = symtab.Lookup (frame.TargetLocation);

				if (source != null) {
					current_frame.SourceLocation = source;
					break;
				}
			}

			long address = current_frame.TargetLocation.Address;

			switch (step_mode) {
			case StepMode.RUN:
			case StepMode.STEP_ONE:
				send_event = true;
				break;

			case StepMode.STEP_LINE_2:				
			case StepMode.STEP_LINE:
				Console.WriteLine ("CHECK: " + address + " " + step_start + " " +
						   step_range + " " + (step_start + step_range - address));

				if ((address < step_start) || (address >= step_start + step_range)) {
					send_event = true;
					break;
				}

				Console.WriteLine ("CONTINE!");

				continue_stepping = true;

				break;

			default:
				break;
			}

			if (send_event && (current_frame_event != null))
				current_frame_event (frame);
		}

		void HandleAnnotation (string annotation, string[] args)
		{
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
				current_frame = new StackFrame ();
				break;

			case "frame-address":
				wait_for = WaitForOutput.FRAME_ADDRESS;
				break;

			case "frame-end":
				if (current_frame != null)
					frame_changed (current_frame);
				break;

			case "frames-invalid":
				if (frames_invalid_event != null)
					frames_invalid_event ();
				break;

			case "value-history-value":
				if (wait_for == WaitForOutput.INTEGER_VALUE)
					wait_for = WaitForOutput.INTEGER_VALUE_2;
				else if (wait_for == WaitForOutput.SIGNED_INTEGER_VALUE)
					wait_for = WaitForOutput.SIGNED_INTEGER_VALUE_2;
				else if (wait_for == WaitForOutput.HEX_VALUE)
					wait_for = WaitForOutput.HEX_VALUE_2;
				else if (wait_for == WaitForOutput.LONG_VALUE)
					wait_for = WaitForOutput.LONG_VALUE_2;
				else if (wait_for == WaitForOutput.STRING_VALUE)
					wait_for = WaitForOutput.STRING_VALUE_2;
				else if (wait_for == WaitForOutput.BYTE_VALUE)
					wait_for = WaitForOutput.BYTE_VALUE_2;
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

			case WaitForOutput.FRAME_ADDRESS:
				wait_for = WaitForOutput.FRAME;
				if (current_frame == null)
					break;

				try {
					current_frame.TargetLocation.Address = Int64.Parse (
						line.Substring (2), NumberStyles.HexNumber);
					return true;
				} catch {
					// FIXME: report error
				}
				break;

			case WaitForOutput.FRAME:
			case WaitForOutput.INTEGER_VALUE:
			case WaitForOutput.SIGNED_INTEGER_VALUE:
			case WaitForOutput.HEX_VALUE:
			case WaitForOutput.LONG_VALUE:
			case WaitForOutput.STRING_VALUE:
			case WaitForOutput.BYTE_VALUE:
				return true;

			case WaitForOutput.INTEGER_VALUE_2:
				wait_for = WaitForOutput.UNKNOWN;
				try {
					last_int_value = UInt32.Parse (line);
					last_value_ok = true;
					return true;
				} catch {
					// FIXME: report error
				}
				return true;

			case WaitForOutput.SIGNED_INTEGER_VALUE_2:
				wait_for = WaitForOutput.UNKNOWN;
				try {
					last_signed_int_value = Int32.Parse (line);
					last_value_ok = true;
					return true;
				} catch {
					// FIXME: report error
				}
				return true;

			case WaitForOutput.BYTE_VALUE_2:
				wait_for = WaitForOutput.UNKNOWN;
				try {
					last_byte_value = Byte.Parse (line);
					last_value_ok = true;
					return true;
				} catch {
					// FIXME: report error
				}
				return true;

			case WaitForOutput.HEX_VALUE_2:
				wait_for = WaitForOutput.UNKNOWN;
				try {
					last_long_value = Int64.Parse (
						line.Substring (2), NumberStyles.HexNumber);
					last_value_ok = true;
					return true;
				} catch {
					// FIXME: report error
				}
				return true;

			case WaitForOutput.LONG_VALUE_2:
				wait_for = WaitForOutput.UNKNOWN;
				try {
					last_long_value = Int64.Parse (line);
					last_value_ok = true;
					return true;
				} catch {
					// FIXME: report error
				}
				return true;

			case WaitForOutput.STRING_VALUE_2:
				wait_for = WaitForOutput.UNKNOWN;
				last_string_value = line;
				last_value_ok = true;
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
			// Console.WriteLine ("SENDING `{0}'", command);
			gdb_event.Reset ();
			gdb_pipe.WriteLine (command);
			gdb_event.WaitOne ();
			// Console.WriteLine ("DONE");
			IdleLoop ();
			// Console.WriteLine ("IDLE-LOOP DONE");
		}

		void start_target (string command, StepMode mode)
		{
			step_mode = mode;

			if (mode != StepMode.STEP_LINE_2) {
				step_start = 0;
				step_range = 0;

				if (current_frame != null) {
					step_start = current_frame.TargetLocation.Address;
					if (current_frame.SourceLocation != null)
						step_range = current_frame.SourceLocation.SourceRange;
				}
			}

			if (((mode == StepMode.STEP_LINE) || (mode == StepMode.STEP_LINE_2)) &&
			    (current_frame != null)) {
				long address = current_frame.TargetLocation.Address;
				long call_target = arch.GetCallTarget (this, address);

				if (call_target != null) {
					Console.WriteLine ("CALL: {0:x} {1:x}", address, call_target);
					send_gdb_command ("x/i $eip");
					send_gdb_command ("x/5b $eip");
				}
			}

			current_frame = null;
			send_gdb_command (command);
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

		ArrayList gdb_output_list = new ArrayList ();
		ArrayList gdb_error_list = new ArrayList ();

		void check_gdb_output (string line, bool is_stderr)
		{
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

		bool IdleLoop ()
		{
			gdb_mutex.WaitOne ();

			foreach (string line in gdb_output_list)
				check_gdb_output (line, false);

			foreach (string line in gdb_error_list)
				check_gdb_output (line, true);

			gdb_output_list.Clear ();
			gdb_error_list.Clear ();

			gdb_mutex.ReleaseMutex ();

			if (continue_stepping) {
				continue_stepping = false;
				start_target ("stepi", StepMode.STEP_LINE_2);
			}

			return true;
		}

		void check_gdb_output ()
		{
			while (true) {
				string line = read_one_line (gdb_output);

				if (line == "")
					continue;
				if (line == null)
					break;

				gdb_mutex.WaitOne ();

				if (line == "\x1a\x1aprompt")
					gdb_event.Set ();

				gdb_output_list.Add (line);

				gdb_mutex.ReleaseMutex ();
			}
		}

		void check_gdb_errors ()
		{
			while (true) {
				string line = read_one_line (gdb_errors);

				if (line == "")
					continue;
				if (line == null)
					break;

				gdb_mutex.WaitOne ();

				if (line == "\x1a\x1aprompt")
					gdb_event.Set ();

				gdb_error_list.Add (line);

				gdb_mutex.ReleaseMutex ();
			}
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
