using GLib;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.CSharp.Debugger;

namespace Mono.Debugger.Backends
{
	public class GDB : IDebuggerBackend, ILanguageCSharp, IDisposable
	{
		public readonly string Path_GDB		= "/usr/bin/gdb";
		public readonly string Path_Mono	= "mono";
		public readonly string Environment_Path	= "/usr/bin";

		Spawn process;
		IOOutputChannel gdb_input;
		IOInputChannel gdb_output;
		IOInputChannel gdb_errors;

		Hashtable symbols;
		Hashtable breakpoints;

		Assembly application;

		ISourceFileFactory source_file_factory;

		bool gdb_event = false;
		bool main_iteration_running = false;

		IdleHandler idle_handler = null;

		IArchitecture arch;

		ArrayList symtabs;
		uint symtab_generation = 0;

		long generic_trampoline_code = 0;

		int last_breakpoint_id;

		readonly uint target_address_size;
		readonly uint target_integer_size;
		readonly uint target_long_integer_size;

		public GDB (string application, string[] arguments)
			: this (application, arguments, new SourceFileFactory ())
		{ }

		public GDB (string application, string[] arguments, ISourceFileFactory source_factory)
		{
			NameValueCollection settings = ConfigurationSettings.AppSettings;

			foreach (string key in settings.AllKeys) {
				string value = settings [key];

				switch (key) {
				case "mono-path":
					Path_Mono = value;
					break;

				case "gdb-path":
					Path_GDB = value;
					break;

				case "environment-path":
					Environment_Path = value;
					break;

				default:
					break;
				}
			}

			this.source_file_factory = source_factory;
			this.application = Assembly.LoadFrom (application);

			MethodInfo main = this.application.EntryPoint;
			string main_name = main.DeclaringType + ":" + main.Name;

			string[] argv = { Path_GDB, "-n", "-nw", "-q", "--annotate=2", "--async",
					  "--args", Path_Mono, "--break", main_name, "--debug=mono",
					  "--noinline", "--nols", "--precompile", "@" + application,
					  "--debug-args", "internal_mono_debugger",
					  application };
			string[] envp = { "PATH=" + Environment_Path };
			string working_directory = ".";

			arch = new ArchitectureI386 ();

			symbols = new Hashtable ();
			breakpoints = new Hashtable ();
			symtabs = new ArrayList ();

			process = new Spawn (working_directory, argv, envp, out gdb_input, out gdb_output,
					     out gdb_errors);

			gdb_output.ReadLine += new ReadLineHandler (check_gdb_output);
			gdb_errors.ReadLine += new ReadLineHandler (check_gdb_errors);

			while (!gdb_event)
				MainIteration ();

			target_address_size = ReadInteger ("print sizeof (void*)");
			target_integer_size = ReadInteger ("print sizeof (long)");
			target_long_integer_size = ReadInteger ("print sizeof (long long)");

			gdb_input.WriteLine ("set prompt");
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
			start_target (StepMode.RUN);
		}

		public void Quit ()
		{
			gdb_input.WriteLine ("quit");
		}

		public void Abort ()
		{
			gdb_input.WriteLine ("signal SIGTERM");
		}

		public void Kill ()
		{
			gdb_input.WriteLine ("kill");
		}

		public void Frame ()
		{
			current_frame = null;
			send_gdb_command ("frame");
		}

		public void Step ()
		{
			start_target (StepMode.STEP_LINE);
		}
		
		public void Next ()
		{
			start_target (StepMode.SKIP_CALLS);
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
			public long Address;

			internal TargetLocation ()
				: this (-1)
			{ }

			public TargetLocation (long Address)
			{
				this.Address = Address;
			}

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
			SKIP_CALLS
		}

		StepMode step_mode = StepMode.STEP_ONE;

		WaitForOutput wait_for = WaitForOutput.UNKNOWN;

		StackFrame current_frame = null;
		StackFrame new_frame = null;
		string source_file = null;

		byte last_byte_value;
		int last_signed_int_value;
		uint last_int_value;
		long last_long_value;
		string last_string_value;
		bool last_value_ok;

		void update_symbol_files ()
		{
			uint generation = ReadInteger ("print/u mono_debugger_symbol_file_table_generation");
			if (generation == symtab_generation)
				return;

			uint modified = ReadInteger ("call mono_debugger_update_symbol_file_table()");
			if (modified == 0)
				return;

			generic_trampoline_code = ReadAddress ("print/a mono_generic_trampoline_code");

			symtabs = new ArrayList ();

			long original = ReadAddress ("print/a mono_debugger_symbol_file_table");
			long ptr = original;
			uint size = ReadInteger (ptr);
			ptr += target_integer_size;
			uint count = ReadInteger (ptr);
			ptr += target_integer_size;
			symtab_generation = ReadInteger (ptr);
			ptr += target_integer_size;

			for (uint i = 0; i < count; i++) {
				long magic = ReadLongInteger (ptr);
				if (magic != OffsetTable.Magic)
					throw new SymbolTableException ();
				ptr += target_long_integer_size;

				uint version = ReadInteger (ptr);
				version = ReadInteger (ptr);
				if (version != OffsetTable.Version)
					throw new SymbolTableException ();
				ptr += target_integer_size;

				uint is_dynamic = ReadInteger (ptr);
				ptr += target_integer_size;

				string image_file = ReadString (ptr);
				ptr += target_address_size;

				long raw_contents = ReadAddress (ptr);
				ptr += target_address_size;

				uint raw_contents_size = ReadInteger (ptr);
				ptr += target_integer_size;

				long address_table = ReadAddress (ptr);
				ptr += target_address_size;

				uint address_table_size = ReadInteger (ptr);
				ptr += target_integer_size + target_address_size;

				if ((raw_contents_size == 0) || (address_table_size == 0)) {
					Console.WriteLine ("IGNORING SYMTAB");
					continue;
				}

				string tmpfile, tmpfile2;
				BinaryReader reader = GetTargetMemoryReader (
					raw_contents, raw_contents_size, out tmpfile);
				BinaryReader address_reader = GetTargetMemoryReader (
					address_table, address_table_size, out tmpfile2);
				
				Console.WriteLine ("SYMTAB: {0:x} {1} {2} - {3:x} {4} {5} - {6}",
						   raw_contents, raw_contents_size, tmpfile,
						   address_table, address_table_size, tmpfile2,
						   image_file);

				MonoSymbolTableReader symreader = new MonoSymbolTableReader (
					image_file, reader, address_reader);
				reader.Close ();
				address_reader.Close ();
				File.Delete (tmpfile);
				File.Delete (tmpfile2);

				symtabs.Add (new CSharpSymbolTable (symreader, source_file_factory));

			}
		}

		// <remarks>
		//   This idle handler must be executed while gdb is not running; we cannot issue any
		//   gdb commands while gdb is running since we'd ge a deadlock, that's why we need to
		//   do this here.
		// </remarks>
		public bool IdleHandler ()
		{
			if (main_iteration_running)
				return true;

			//
			// The stack frame has changed, check whether we must continue single-stepping
			// or whether we can report that the target has stopped.
			//
			if (new_frame != current_frame)
				frame_changed ();

			//
			// If there's no more pending work to do, we can safely remove the idle handler.
			// It'll be reinstalled when it's needed.
			//

			idle_handler = null;
			return false;
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
			return ReadString ("print (char *)*" + address);
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

		ISourceLocation LookupAddress (long address)
		{
			return LookupAddress (new TargetLocation (address));
		}

		ISourceLocation LookupAddress (ITargetLocation address)
		{
			foreach (ISymbolTable symtab in symtabs) {
				ISourceLocation source = symtab.Lookup (address);

				if (source != null)
					return source;
			}

			return null;
		}

		void frame_changed ()
		{
			bool send_event = false;

			update_symbol_files ();

			int step_range = 0;
			long start_address = 0;
			if (current_frame != null) {
				start_address = current_frame.TargetLocation.Address;
				step_range = current_frame.SourceLocation.SourceRange;
			}

		again:
			current_frame = new_frame;

			long address = current_frame.TargetLocation.Address;

			switch (step_mode) {
			case StepMode.RUN:
			case StepMode.STEP_ONE:
				send_event = true;
				break;

			case StepMode.STEP_LINE:
			case StepMode.SKIP_CALLS:
				if ((address < start_address) || (address >= start_address + step_range)) {
					step_mode = StepMode.RUN;
					change_target_state (TargetState.STOPPED);
					send_event = true;
					break;
				}

				start_target (step_mode);
				goto again;

			default:
				break;
			}

			ISourceLocation source = LookupAddress (current_frame.TargetLocation);
			if (source != null)
				current_frame.SourceLocation = source;

			if (send_event && (current_frame_event != null))
				current_frame_event (current_frame);
		}

		void change_target_state (TargetState new_state)
		{
			if (new_state == target_state)
				return;

			// Don't signal STOPPED to the GUI if we're still stepping.
			if ((step_mode == StepMode.STEP_LINE) || (step_mode == StepMode.SKIP_CALLS)) {
				if (new_state == TargetState.STOPPED)
					return;
			}

			target_state = new_state;

			if (state_changed != null)
				state_changed (target_state);
		}

		void HandleAnnotation (string annotation, string[] args)
		{
			switch (annotation) {
			case "starting":
				change_target_state (TargetState.RUNNING);
				break;

			case "stopped":
				if (target_state != TargetState.RUNNING)
					break;
				change_target_state (TargetState.STOPPED);
				break;

			case "exited":
				change_target_state (TargetState.EXITED);
				break;

			case "prompt":
				wait_for = WaitForOutput.UNKNOWN;
				gdb_event = true;
				break;

			case "breakpoint":
				wait_for = WaitForOutput.BREAKPOINT;
				break;

			case "frame-begin":
				wait_for = WaitForOutput.FRAME;
				new_frame = new StackFrame ();
				break;

			case "frame-end":
				if (idle_handler == null)
					idle_handler = new IdleHandler (new GSourceFunc (IdleHandler));
				break;

			case "frame-address":
				wait_for = WaitForOutput.FRAME_ADDRESS;
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
				if (new_frame == null)
					break;

				try {
					new_frame.TargetLocation.Address = Int64.Parse (
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
				last_string_value = line.Substring (11, line.Length - 12);
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
			gdb_event = false;
			gdb_input.WriteLine (command);
			while (!gdb_event)
				MainIteration ();
		}

		void start_target (StepMode mode)
		{
			string command;

			step_mode = mode;
			switch (mode) {
			case StepMode.STEP_ONE:
			case StepMode.STEP_LINE:	
				command = "stepi";
				break;

			case StepMode.SKIP_CALLS:
				command = "nexti";
				break;

			default:
				if (target_state == TargetState.NO_TARGET)
					command = "run";
				else
					command = "continue";
				break;
			}

			if ((current_frame == null) ||
			    ((mode != StepMode.STEP_LINE) && (mode != StepMode.SKIP_CALLS))) {
				new_frame = null;
				send_gdb_command (command);
				return;
			}

			long address = current_frame.TargetLocation.Address;
			int insn_size;
			long call_target = arch.GetCallTarget (this, address, out insn_size);
			long stop_address = 0;

			if (call_target == 0) {
				new_frame = null;
				send_gdb_command (command);
				return;
			}

			Console.WriteLine ("CALL TARGET: {0:x} {1}", call_target, insn_size);
			if (mode == StepMode.STEP_LINE) {
				long trampoline = arch.GetTrampoline (
					this, call_target, generic_trampoline_code);

				if (trampoline != 0) {
					Console.WriteLine ("TRAMPOLINE: {0:x}", trampoline);

					long method = ReadAddress (
						"call/a mono_compile_method (" + trampoline + ")");

					Console.WriteLine ("COMPILED: {0:x}", method);

					update_symbol_files ();

					ISourceLocation source = LookupAddress (method);
					if (source == null)
						stop_address = address + insn_size;
					else {
						stop_address = method;
						Console.WriteLine ("TRAMPOLINE CALL: {0}", source);
					}
				} else {
					ISourceLocation source = LookupAddress (call_target);
					if (source == null)
						stop_address = address + insn_size;
					else
						Console.WriteLine ("CALL: {0}", source);
				}
			} else
				stop_address = address + insn_size;

			if (stop_address != 0) {
				send_gdb_command ("tbreak *" + stop_address);
				command = "continue";
				// step_mode = StepMode.RUN;
			}

			new_frame = null;
			send_gdb_command (command);
		}

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

		void check_gdb_output (string line)
		{
			// Console.WriteLine ("OUTPUT: {0}", line);
			check_gdb_output (line, false);
		}

		void check_gdb_errors (string line)
		{
			// Console.WriteLine ("ERROR: {0}", line);
			check_gdb_output (line, true);
		}

		void MainIteration ()
		{
			main_iteration_running = true;
			g_main_context_iteration (IntPtr.Zero, true);
			main_iteration_running = false;
		}

		[DllImport("glib-2.0")]
		static extern void g_main_context_iteration (IntPtr context, bool may_block);

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
