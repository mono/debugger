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

	public class DebuggerBackend : ITargetNotification, ISymbolLookup, IDisposable
	{
		public readonly string Path_Mono	= "mono";
		public readonly string Environment_Path	= "/usr/bin";

		SymbolTableCollection symtabs;

		IInferior inferior;
		ILanguageBackend language;

		string[] argv;
		string[] envp;
		string target_application;
		string working_directory;

		StackFrame current_frame;
		StackFrame[] current_backtrace;
		IMethod current_method;

		bool load_native_symtab = false;

		bool step_line;
		bool next_line;
		StepFrame current_step_frame;
		bool must_send_update;
		bool native;

		public DebuggerBackend ()
			: this (false)
		{ }

		public DebuggerBackend (bool native)
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

		// <summary>
		//   If true, load the target's native symbol table.  You need to enable this
		//   to debug native C applications, but you can safely disable it if you just
		//   want to debug managed C# code.
		// </summary>
		public bool LoadNativeSymbolTable {
			get {
				return load_native_symtab;
			}

			set {
				load_native_symtab = value;
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
			if (new_state == TargetState.STOPPED) {
				if (busy) {
					busy = false;
					return;
				}
					
				if (!frame_changed ())
					return;
			}

			if (new_state == TargetState.BUSY) {
				busy = true;
				return;
			}

			busy = false;

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
		public event ModulesChangedHandler ModulesChangedEvent;

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
			if (native)
				load_native_symtab = true;
			inferior = new PTraceInferior (working_directory, argv, envp, native,
						       load_native_symtab);
			inferior.TargetExited += new TargetExitedHandler (child_exited);
			inferior.TargetOutput += new TargetOutputHandler (inferior_output);
			inferior.TargetError += new TargetOutputHandler (inferior_errors);
			inferior.StateChanged += new StateChangedHandler (target_state_changed);

			symtabs = new SymbolTableCollection ();
			Console.WriteLine ("RUN: {0} {1}", native, load_native_symtab);
			if (load_native_symtab)
				symtabs.AddSymbolTable (inferior.SymbolTable);

			if (native)
				return;

			language = new MonoCSharpLanguageBackend (this, inferior);
			symtabs.AddSymbolTable (language.SymbolTable);
			inferior.ApplicationSymbolTable = language.SymbolTable;
			language.ModulesChangedEvent += new ModulesChangedHandler (modules_changed);
		}

		void modules_changed ()
		{
			if (ModulesChangedEvent != null)
				ModulesChangedEvent ();
		}

		void load_core (string core_file, string[] argv)
		{
			inferior = new CoreFileElfI386 (argv [0], core_file);

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

		StepFrame get_step_frame ()
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

		StepFrame get_simple_step_frame (StepMode mode)
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
			step_line = true;
			inferior.Step (get_step_frame ());
		}

		public void NextLine ()
		{
			check_can_run ();
			StepFrame frame = get_step_frame ();
			if (frame == null) {
				inferior.Step (get_simple_step_frame (StepMode.NextInstruction));
				return;
			}

			next_line = true;
			inferior.Step (new StepFrame (
				frame.Start, frame.End, null, StepMode.Finish));
		}

		public void Continue ()
		{
			check_can_run ();
			inferior.Continue ();
		}

		public void Continue (TargetAddress until)
		{
			check_can_run ();
			TargetAddress current = CurrentFrameAddress;

			Console.WriteLine (String.Format ("Requested to run from {0:x} until {1:x}.",
							  current, until));

			while (current < until)
				current += inferior.Disassembler.GetInstructionSize (current);

			if (current != until)
				Console.WriteLine (String.Format (
					"Oooops: reached {0:x} but symfile had {1:x}",
					current, until));

			inferior.Continue (until);
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

			current_step_frame = new StepFrame (
				frame.Method.StartAddress, frame.Method.EndAddress, null, StepMode.Finish);
			inferior.Step (current_step_frame);
		}

		public void TestBreakpoint (string name)
		{
			MonoCSharpLanguageBackend csharp = language as MonoCSharpLanguageBackend;
			if (csharp == null)
				throw new InternalError ();

			Console.WriteLine ("TEST: {0}", name);

			csharp.InsertBreakpoint (name);
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
					SourceLocation source = method.Source.Lookup (address);
					current_backtrace [i] = new StackFrame (
						inferior, address, frames [i], source, method);
				} else
					current_backtrace [i] = new StackFrame (
						inferior, address, frames [i]);
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

		public IModule[] Modules {
			get {
				check_disposed ();
				ArrayList modules = new ArrayList ();
				if (inferior != null)
					modules.AddRange (inferior.Modules);
				if (language != null)
					modules.AddRange (language.Modules);
				IModule[] retval = new IModule [modules.Count];
				modules.CopyTo (retval, 0);
				return retval;
			}
		}

		static bool in_frame_changed = false;

		bool frame_changed ()
		{
			if (in_frame_changed)
				throw new InternalError ();

			in_frame_changed = true;
			bool retval = do_frame_changed ();
			in_frame_changed = false;
			return retval;
		}

		bool do_frame_changed ()
		{
			IMethod old_method = current_method;
			bool old_step_line = step_line;
			bool old_next_line = next_line;

			IInferiorStackFrame[] frames = inferior.GetBacktrace (1, true);
			TargetAddress address = frames [0].Address;

			if ((language != null) && !language.BreakpointHit (address))
				return false;

			if ((current_step_frame != null) &&
			    ((address >= current_step_frame.Start) &&
			     (address < current_step_frame.End))) {
				inferior.Step (current_step_frame);
				return false;
			}

			step_line = false;
			next_line = false;
			current_step_frame = null;

			if (!must_send_update && (current_frame != null) && current_frame.IsValid &&
			    (current_frame.TargetAddress == address))
				return true;

			frames_invalid ();

			if ((current_method == null) ||
			    (!MethodBase.IsInSameMethod (current_method, address))) {
				symtabs.UpdateSymbolTable ();
				current_method = Lookup (address);
			}

			// If some clown requested a backtrace while doing the symbol lookup ....
			frames_invalid ();

			if ((current_method != null) && current_method.HasSource) {
				SourceLocation source = current_method.Source.Lookup (address);

				if (old_step_line || old_next_line) {
					if ((source.SourceOffset > 0) && (source.SourceRange > 0)) {
						must_send_update = true;

						inferior.Step (new StepFrame (
							address - source.SourceOffset,
							address + source.SourceRange, language,
							old_next_line ? StepMode.Finish : StepMode.StepFrame));
						return false;
					} else if (current_method.HasMethodBounds &&
						   (address < current_method.MethodStartAddress)) {
						must_send_update = true;

						inferior.Step (new StepFrame (
							current_method.StartAddress,
							current_method.MethodStartAddress,
							null, StepMode.Finish));
						return false;
					}
				}

				current_frame = new StackFrame (
					inferior, address, frames [0], source, current_method);
			} else
				current_frame = new StackFrame (
					inferior, address, frames [0]);

			if (must_send_update || (current_method != old_method)) {
				if (current_method != null) {
					if (MethodChangedEvent != null)
						MethodChangedEvent (current_method);
				} else {
					if (MethodInvalidEvent != null)
						MethodInvalidEvent ();
				}
			}

			must_send_update = false;

			if (FrameChangedEvent != null)
				FrameChangedEvent (current_frame);

			return true;
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
