using GLib;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.CSharp;
using Mono.Debugger.Architecture;

namespace Mono.Debugger
{
	public delegate void MethodInvalidHandler ();
	public delegate void MethodChangedHandler (IMethod method);

	public delegate void StackFrameHandler (StackFrame frame);
	public delegate void StackFrameInvalidHandler ();

	internal class TargetInfo : ITargetInfo
	{
		int target_int_size;
		int target_long_size;
		int target_address_size;

		internal TargetInfo (int target_address_size)
			: this (4, 8, target_address_size)
		{ }

		internal TargetInfo (int target_int_size, int target_long_size,
				     int target_address_size)
		{
			this.target_int_size = target_int_size;
			this.target_long_size = target_long_size;
			this.target_address_size = target_address_size;
		}

		int ITargetInfo.TargetIntegerSize {
			get {
				return target_int_size;
			}
		}

		int ITargetInfo.TargetLongIntegerSize {
			get {
				return target_long_size;
			}
		}

		int ITargetInfo.TargetAddressSize {
			get {
				return target_address_size;
			}
		}
	}

	internal class TargetReader : ITargetMemoryReader
	{
		byte[] data;
		TargetBinaryReader reader;
		int offset;
		IInferior inferior;

		internal TargetReader (byte[] data, IInferior inferior)
		{
			this.reader = new TargetBinaryReader (data, inferior);
			this.inferior = inferior;
			this.data = data;
			this.offset = 0;
		}

		public long Offset {
			get {
				return reader.Position;
			}

			set {
				reader.Position = value;
			}
		}

		public long Size {
			get {
				return data.Length;
			}
		}

		public byte[] Contents {
			get {
				return data;
			}
		}

		public TargetBinaryReader BinaryReader {
			get {
				return reader;
			}
		}

		public int TargetIntegerSize {
			get {
				return inferior.TargetIntegerSize;
			}
		}

		public int TargetLongIntegerSize {
			get {
				return inferior.TargetLongIntegerSize;
			}
		}

		public int TargetAddressSize {
			get {
				return inferior.TargetAddressSize;
			}
		}

		public byte ReadByte ()
		{
			return reader.ReadByte ();
		}

		public int ReadInteger ()
		{
			return reader.ReadInt32 ();
		}

		public long ReadLongInteger ()
		{
			return reader.ReadInt64 ();
		}

		public TargetAddress ReadAddress ()
		{
			if (TargetAddressSize == 4)
				return new TargetAddress (inferior, reader.ReadInt32 ());
			else if (TargetAddressSize == 8)
				return new TargetAddress (inferior, reader.ReadInt64 ());
			else
				throw new TargetMemoryException (
					"Unknown target address size " + TargetAddressSize);
		}

		public override string ToString ()
		{
			StringBuilder sb = new StringBuilder ();

			sb.Append (String.Format ("MemoryReader (["));
			for (int i = 0; i < data.Length; i++) {
				if (i > 0)
					sb.Append (" ");
				sb.Append (String.Format ("{1}{0:x}", data [i], data [i] >= 16 ? "" : "0"));
			}
			sb.Append ("])");
			return sb.ToString ();
		}
	}

	internal class StepFrame : IStepFrame
	{
		TargetAddress start, end;
		ILanguageBackend language;
		StepMode mode;

		internal StepFrame (ILanguageBackend language, StepMode mode)
			: this (TargetAddress.Null, TargetAddress.Null, language, mode)
		{ }

		internal StepFrame (TargetAddress start, TargetAddress end, ILanguageBackend language,
				    StepMode mode)
		{
			this.start = start;
			this.end = end;
			this.language = language;
			this.mode = mode;
		}

		public StepMode Mode {
			get {
				return mode;
			}
		}

		public TargetAddress Start {
			get {
				return start;
			}
		}

		public TargetAddress End {
			get {
				return end;
			}
		}

		public ILanguageBackend Language {
			get {
				return language;
			}
		}

		public override string ToString ()
		{
			return String.Format ("StepFrame ({0:x},{1:x},{2},{3})",
					      Start, End, Mode, Language);
		}
	}

	internal delegate void TargetAsyncCallback (object user_data, object result);

	internal class TargetAsyncResult
	{
		object user_data, result;
		bool completed;
		TargetAsyncCallback callback;

		internal TargetAsyncResult (TargetAsyncCallback callback, object user_data)
		{
			this.callback = callback;
			this.user_data = user_data;
		}

		public void Completed (object result)
		{
			if (completed)
				throw new InvalidOperationException ();

			completed = true;
			if (callback != null)
				callback (user_data, result);

			this.result = result;
		}

		public object AsyncResult {
			get {
				return result;
			}
		}

		public bool IsCompleted {
			get {
				return completed;
			}
		}
	}

	public class DebuggerBackend : ITargetNotification, ISymbolLookup, IDisposable
	{
		public readonly string Path_Mono	= "mono";
		public readonly string Environment_Path	= "/usr/bin";

		ISourceFileFactory source_factory;
		ISymbolTableCollection symtabs;

		IInferior inferior;
		ILanguageBackend language;

		string[] argv;
		string[] envp;
		string target_application;
		string working_directory;

		StackFrame current_frame;
		StackFrame[] current_backtrace;
		IMethod current_method;

		bool native;

		public DebuggerBackend (ISourceFileFactory source_factory)
			: this (source_factory, false)
		{ }

		public DebuggerBackend (ISourceFileFactory source_factory, bool native)
		{
			NameValueCollection settings = ConfigurationSettings.AppSettings;

			foreach (string key in settings.AllKeys) {
				string value = settings [key];

				switch (key) {
				case "mono-path":
					Path_Mono = value;
					break;

				case "environment-path":
					Environment_Path = value;
					break;

				default:
					break;
				}
			}

			this.source_factory = source_factory;
			this.native = native;
		}

		public string CurrentWorkingDirectory {
			get {
				return working_directory;
			}

			set {
				if (inferior != null)
					throw new AlreadyHaveTargetException ();

				working_directory = value;
			}
		}

		public string[] CommandLineArguments {
			get {
				return argv;
			}

			set {
				if (inferior != null)
					throw new AlreadyHaveTargetException ();

				argv = value;
			}
		}

		public string TargetApplication {
			get {
				return target_application;
			}

			set {
				if (inferior != null)
					throw new AlreadyHaveTargetException ();

				target_application = value;
			}
		}

		public string[] Environment {
			get {
				return envp;
			}

			set {
				if (inferior != null)
					throw new AlreadyHaveTargetException ();

				envp = value;
			}
		}

		//
		// ITargetNotification
		//

		bool busy = false;
		public TargetState State {
			get {
				if (busy)
					return TargetState.BUSY;
				else if (inferior == null)
					return TargetState.NO_TARGET;
				else
					return inferior.State;
			}
		}

		bool DebuggerBusy {
			get {
				return busy;
			}

			set {
				if (busy == value)
					return;

				busy = value;
				if (StateChanged != null)
					StateChanged (State, 0);
			}
		}

		void frames_invalid ()
		{
			if (current_frame != null) {
				current_frame.Dispose ();
				current_frame = null;
			}

			if (current_backtrace != null) {
				foreach (StackFrame frame in current_backtrace)
					frame.Dispose ();
				current_backtrace = null;
			}
		}

		void target_state_changed (TargetState new_state, int arg)
		{
			if (new_state == TargetState.STOPPED)
				frame_changed ();

			if (StateChanged != null)
				StateChanged (new_state, arg);
		}

		public event TargetOutputHandler TargetOutput;
		public event TargetOutputHandler TargetError;
		public event StateChangedHandler StateChanged;
		public event TargetExitedHandler TargetExited;

		public event MethodInvalidHandler MethodInvalidEvent;
		public event MethodChangedHandler MethodChangedEvent;
		public event StackFrameHandler FrameChangedEvent;
		public event StackFrameInvalidHandler FramesInvalidEvent;

		public IInferior Inferior {
			get {
				check_disposed ();
				return inferior;
			}
		}

		public bool HasTarget {
			get {
				check_disposed ();
				return inferior != null;
			}
		}

		void child_exited ()
		{
			inferior.Dispose ();
			inferior = null;
			language = null;
			symtabs = null;
			current_method = null;
			frames_invalid ();
			if (FramesInvalidEvent != null)
				FramesInvalidEvent ();
			if (TargetExited != null)
				TargetExited ();
		}

		void inferior_output (string line)
		{
			if (TargetOutput != null)
				TargetOutput (line);
		}

		void inferior_errors (string line)
		{
			if (TargetError != null)
				TargetError (line);
		}

		public void Run ()
		{
			check_disposed ();
			do_run ((string) null);
		}

		public void ReadCoreFile (string core_file)
		{
			check_disposed ();
			do_run (core_file);
		}

		void do_run (string core_file)
		{
			if (inferior != null)
				throw new AlreadyHaveTargetException ();

			if (target_application == null)
				throw new CannotStartTargetException ("You must specify a program to debug.");

			if (!native) {
				try {
					Assembly application = Assembly.LoadFrom (target_application);
					if (application != null) {
						do_run (target_application, core_file, application);
						return;
					}
				} catch (Exception e) {
					Console.WriteLine ("EXCEPTION: {0}", e);
					if (core_file != null)
						return;
				}
			}

			setup_environment ();

			string[] new_argv = new string [argv.Length + 1];
			new_argv [0] = target_application;
			argv.CopyTo (new_argv, 1);

			native = true;
			if (core_file != null)
				load_core (core_file, new_argv);
			else
				do_run (new_argv);
		}

		void setup_environment ()
		{
			if (argv == null)
				argv = new string [0];
			if (envp == null)
				envp = new string[] { "PATH=" + Environment_Path, "LD_BIND_NOW=yes" };
			if (working_directory == null)
				working_directory = ".";
		}

		void do_run (string target_application, string core_file, Assembly application)
		{
			MethodInfo main = application.EntryPoint;
			string main_name = main.DeclaringType + ":" + main.Name;

			setup_environment ();

			string[] start_argv = {
				Path_Mono, "--break", main_name, "--debug=mono",
				"--noinline", "--nols", "--debug-args", "internal_mono_debugger",
				target_application };

			string[] new_argv = new string [argv.Length + start_argv.Length];
			start_argv.CopyTo (new_argv, 0);
			argv.CopyTo (new_argv, start_argv.Length);

			native = false;
			if (core_file != null)
				load_core (core_file, new_argv);
			else
				do_run (new_argv);
		}

		void do_run (string[] argv)
		{
			inferior = new Inferior (working_directory, argv, envp, native, source_factory);
			inferior.TargetExited += new TargetExitedHandler (child_exited);
			inferior.TargetOutput += new TargetOutputHandler (inferior_output);
			inferior.TargetError += new TargetOutputHandler (inferior_errors);
			inferior.StateChanged += new StateChangedHandler (target_state_changed);

			symtabs = new SymbolTableCollection ();
			symtabs.AddSymbolTable (inferior.SymbolTable);

			if (!native) {
				language = new MonoCSharpLanguageBackend (this, inferior);
				symtabs.AddSymbolTable (language.SymbolTable);
				inferior.ApplicationSymbolTable = language.SymbolTable;
			}
		}

		void load_core (string core_file, string[] argv)
		{
			Console.WriteLine ("CORE: {0} {1}", argv [0], core_file);
			inferior = new CoreFileElfI386 (argv [0], core_file, source_factory);

			symtabs = new SymbolTableCollection ();
			symtabs.AddSymbolTable (inferior.SymbolTable);

			if (!native) {
				language = new MonoCSharpLanguageBackend (this, inferior);
				symtabs.AddSymbolTable (language.SymbolTable);
				inferior.ApplicationSymbolTable = language.SymbolTable;
				symtabs.UpdateSymbolTable ();
			}

			frame_changed ();
		}

		public void Quit ()
		{
			if (inferior != null)
				inferior.Shutdown ();
		}

		void check_inferior ()
		{
			check_disposed ();
			if (inferior == null)
				throw new NoTargetException ();
		}

		void check_stopped ()
		{
			check_inferior ();

			if ((State != TargetState.STOPPED) && (State != TargetState.CORE_FILE))
				throw new TargetNotStoppedException ();
		}

		void check_can_run ()
		{
			check_inferior ();

			if (State == TargetState.CORE_FILE)
				throw new CannotExecuteCoreFileException ();
			else if (State != TargetState.STOPPED)
				throw new TargetNotStoppedException ();
		}

		IStepFrame get_step_frame ()
		{
			check_inferior ();
			StackFrame frame = CurrentFrame;
			ILanguageBackend language = (frame.Method != null) ? frame.Method.Language : null;

			if (frame.SourceLocation == null)
				return null;

			int offset = frame.SourceLocation.SourceOffset;
			int range = frame.SourceLocation.SourceRange;

			TargetAddress start = frame.TargetAddress - offset;
			TargetAddress end = frame.TargetAddress + range;

			return new StepFrame (start, end, language, StepMode.StepFrame);
		}

		IStepFrame get_simple_step_frame (StepMode mode)
		{
			check_inferior ();
			StackFrame frame = CurrentFrame;
			ILanguageBackend language = (frame.Method != null) ? frame.Method.Language : null;

			return new StepFrame (language, mode);
		}

		public void StepInstruction ()
		{
			check_can_run ();
			inferior.Step (get_simple_step_frame (StepMode.SingleInstruction));
		}

		public void NextInstruction ()
		{
			check_can_run ();
			inferior.Step (get_simple_step_frame (StepMode.NextInstruction));
		}

		public void StepLine ()
		{
			check_can_run ();
			inferior.Step (get_step_frame ());
		}

		public void NextLine ()
		{
			check_can_run ();
			IStepFrame frame = get_step_frame ();
			if (frame == null) {
				inferior.Step (get_simple_step_frame (StepMode.NextInstruction));
				return;
			}

			inferior.Step (new StepFrame (
				frame.Start, frame.End, null, StepMode.Finish));
		}

		public void Continue ()
		{
			check_can_run ();
			inferior.Continue ();
		}

		public void Stop ()
		{
			check_inferior ();
			inferior.Stop ();
		}

		public void Finish ()
		{
			check_can_run ();
			StackFrame frame = CurrentFrame;
			if (frame.Method == null)
				throw new NoMethodException ();

			inferior.Step (new StepFrame (
				frame.Method.StartAddress, frame.Method.EndAddress, null, StepMode.Finish));
		}

		public TargetAddress CurrentFrameAddress {
			get {
				check_stopped ();
				return inferior.CurrentFrame;
			}
		}

		public StackFrame CurrentFrame {
			get {
				check_stopped ();
				return current_frame;
			}
		}

		public IMethod CurrentMethod {
			get {
				check_stopped ();
				if (current_method == null)
					throw new NoMethodException ();
				return current_method;
			}
		}

		public StackFrame[] GetBacktrace ()
		{
			check_stopped ();

			if (current_backtrace != null)
				return current_backtrace;

			symtabs.UpdateSymbolTable ();

			IInferiorStackFrame[] frames = inferior.GetBacktrace (-1, false);
			current_backtrace = new StackFrame [frames.Length];

			for (int i = 0; i < frames.Length; i++) {
				TargetAddress address = frames [i].Address;

				IMethod method = Lookup (address);
				if ((method != null) && method.HasSource) {
					ISourceLocation source = method.Source.Lookup (address);
					current_backtrace [i] = new StackFrame (
						this, inferior, address, frames [i], source, method);
				} else
					current_backtrace [i] = new StackFrame (
						this, inferior, address, frames [i]);
			}

			return current_backtrace;
		}

		public long GetRegister (int register)
		{
			check_stopped ();
			return inferior.GetRegister (register);
		}

		public long[] GetRegisters (int[] registers)
		{
			check_stopped ();
			return inferior.GetRegisters (registers);
		}

		public IDisassembler Disassembler {
			get {
				check_inferior ();
				return inferior.Disassembler;
			}
		}

		public IArchitecture Architecture {
			get {
				check_inferior ();
				return inferior.Architecture;
			}
		}

		public ITargetMemoryAccess TargetMemoryAccess {
			get {
				check_inferior ();
				return inferior;
			}
		}

		public IMethod Lookup (TargetAddress address)
		{
			return symtabs.Lookup (address);
		}

		void frame_changed ()
		{
			IMethod old_method = current_method;

			IInferiorStackFrame[] frames = inferior.GetBacktrace (1, true);
			TargetAddress address = frames [0].Address;

			if ((current_frame != null) && current_frame.IsValid &&
			    (current_frame.TargetAddress == address))
				return;

			frames_invalid ();

			if ((current_method == null) ||
			    (!MethodBase.IsInSameMethod (current_method, address))) {
				symtabs.UpdateSymbolTable ();
				current_method = Lookup (address);
			}

			if (current_method != old_method) {
				if (current_method != null) {
					if (MethodChangedEvent != null)
						MethodChangedEvent (current_method);
				} else {
					if (MethodInvalidEvent != null)
						MethodInvalidEvent ();
				}
			}

			if ((current_method != null) && current_method.HasSource) {
				ISourceLocation source = current_method.Source.Lookup (address);
				current_frame = new StackFrame (
					this, inferior, address, frames [0], source, current_method);
			} else
				current_frame = new StackFrame (
					this, inferior, address, frames [0]);

			if (FrameChangedEvent != null)
				FrameChangedEvent (current_frame);
		}

		public ISourceFileFactory SourceFileFactory {
			get {
				check_disposed ();
				return source_factory;
			}
		}

		public IBreakPoint AddBreakPoint (ITargetLocation location)
		{
			throw new NotImplementedException ();
		}

		[DllImport("glib-2.0")]
		extern static IntPtr g_main_context_default ();

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Debugger");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					// Do stuff here
					if (inferior != null)
						inferior.Kill ();
				}
				
				// Release unmanaged resources
				this.disposed = true;

				lock (this) {
					// Nothing to do yet.
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~DebuggerBackend ()
		{
			Dispose (false);
		}
	}
}
