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

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.CSharp;
using Mono.Debugger.Architecture;

namespace Mono.Debugger.Backends
{
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
	}

	internal class TargetMemoryStream : Stream
	{
		TargetAddress address;
		ITargetInfo target_info;
		ITargetMemoryAccess memory;
		long position;

		internal TargetMemoryStream (ITargetMemoryAccess memory, TargetAddress address,
					     ITargetInfo target_info)
		{
			this.memory = memory;
			this.address = address;
			this.target_info = target_info;
		}

		public override bool CanRead {
			get {
				return true;
			}
		}

		public override bool CanSeek {
			get {
				return true;
			}
		}

		public override bool CanWrite {
			get {
				return memory.CanWrite;
			}
		}

		public override long Length {
			get {
				throw new NotSupportedException ();
			}
		}

		public override long Position {
			get {
				return position;
			}

			set {
				position = value;
			}
		}

		public override void SetLength (long value)
		{
			throw new NotSupportedException ();
		}

		public override long Seek (long offset, SeekOrigin origin)
		{
                        int ref_point;

                        switch (origin) {
			case SeekOrigin.Begin:
				ref_point = 0;
				break;
			case SeekOrigin.Current:
				ref_point = (int) position;
				break;
			case SeekOrigin.End:
				throw new NotSupportedException ();
			default:
				throw new ArgumentException();
                        }

			// FIXME: The stream would actually allow being seeked before its start.
			//        However, I don't know how our callers would deal with a negative
			//        Position.
			if (ref_point + offset < 0)
                                throw new IOException ("Attempted to seek before start of stream");

                        position = ref_point + offset;

                        return position;
                }

		public override void Flush ()
		{
		}

		public override int Read (byte[] buffer, int offset, int count)
		{
			try {
				byte[] retval = memory.ReadBuffer (address + position, count);
				retval.CopyTo (buffer, offset);
			} catch (Exception e) {
				throw new IOException ("Cannot read target memory", e);
			}

			position += count;
			return count;
		}

		public override void Write (byte[] buffer, int offset, int count)
		{
			try {
				if (offset != 0) {
					byte[] temp = new byte [count];
					Array.Copy (buffer, offset, temp, 0, count);
					memory.WriteBuffer (address + position, temp, count);
				} else
					memory.WriteBuffer (address + position, buffer, count);
			} catch (Exception e) {
				throw new IOException ("Cannot read target memory", e);
			}

			position += count;
		}
	}

	internal class StackFrame : IStackFrame
	{
		IMethod method;
		TargetAddress address;
		IInferior inferior;
		ISourceLocation source;

		public StackFrame (IInferior inferior, TargetAddress address,
				   ISourceLocation source, IMethod method)
			: this (inferior, address)
		{
			this.source = source;
			this.method = method;
		}

		public StackFrame (IInferior inferior, TargetAddress address)
		{
			this.inferior = inferior;
			this.address = address;
		}

		ISourceLocation IStackFrame.SourceLocation {
			get {
				return source;
			}
		}

		TargetAddress IStackFrame.TargetAddress {
			get {
				return address;
			}
		}

		IMethod IStackFrame.Method {
			get {
				return method;
			}
		}

		public override string ToString ()
		{
			StringBuilder builder = new StringBuilder ();

			if (source != null) {
				builder.Append (source);
				builder.Append (" at ");
			}
			builder.Append (address);

			return builder.ToString ();
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

	public class Debugger : IDebuggerBackend, ISymbolLookup, IDisposable
	{
		public readonly string Path_Mono	= "mono";
		public readonly string Environment_Path	= "/usr/bin";

		ISourceFileFactory source_factory;
		ISymbolTableCollection symtabs;

		Inferior inferior;
		ILanguageBackend language;

		readonly uint target_address_size;
		readonly uint target_integer_size;
		readonly uint target_long_integer_size;

		string[] argv;
		string[] envp;
		string target_application;
		string working_directory;

		IStackFrame current_frame;
		IMethod current_method;

		bool native;

		public Debugger (ISourceFileFactory source_factory)
			: this (source_factory, false)
		{ }

		public Debugger (ISourceFileFactory source_factory, bool native)
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

		void target_state_changed (TargetState new_state, int arg)
		{
			current_frame = null;
			if (new_state == TargetState.STOPPED)
				frame_changed ();

			if (StateChanged != null)
				StateChanged (new_state, arg);
		}

		public event TargetOutputHandler TargetOutput;
		public event TargetOutputHandler TargetError;
		public event StateChangedHandler StateChanged;

		//
		// IDebuggerBackend
		//

		public event MethodInvalidHandler MethodInvalidEvent;
		public event MethodChangedHandler MethodChangedEvent;
		public event StackFrameHandler FrameChangedEvent;
		public event StackFramesInvalidHandler FramesInvalidEvent;

		public IInferior Inferior {
			get {
				return inferior;
			}
		}

		public bool HasTarget {
			get {
				return inferior != null;
			}
		}

		void child_exited ()
		{
			inferior.Dispose ();
			inferior = null;
			language = null;
			symtabs = null;
			current_frame = null;
			if (FramesInvalidEvent != null)
				FramesInvalidEvent ();
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
			if (inferior != null)
				throw new AlreadyHaveTargetException ();

			if (target_application == null)
				throw new CannotStartTargetException ("You must specify a program to debug.");

			if (!native) {
				try {
					Assembly application = Assembly.LoadFrom (target_application);
					if (application != null) {
						do_run (target_application, application);
						return;
					}
				} catch {
					// Do nothing.
				}
			}

			setup_environment ();

			string[] new_argv = new string [argv.Length + 1];
			new_argv [0] = target_application;
			argv.CopyTo (new_argv, 1);

			native = true;
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

		void do_run (string target_application, Assembly application)
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
			do_run (new_argv);
		}

		void do_run (string[] argv)
		{
			inferior = new Inferior (working_directory, argv, envp, native, source_factory);
			inferior.ChildExited += new ChildExitedHandler (child_exited);
			inferior.TargetOutput += new TargetOutputHandler (inferior_output);
			inferior.TargetError += new TargetOutputHandler (inferior_errors);
			inferior.StateChanged += new StateChangedHandler (target_state_changed);

			symtabs = new SymbolTableCollection ();
			symtabs.AddSymbolTable (inferior.SymbolTable);

			if (!native) {
				language = new MonoCSharpLanguageBackend (inferior);
				symtabs.AddSymbolTable (language.SymbolTable);
				inferior.ApplicationSymbolTable = language.SymbolTable;
			}
		}

		public void Quit ()
		{
			if (inferior != null)
				inferior.Shutdown ();
		}

		IStepFrame get_step_frame ()
		{
			if (inferior == null)
				throw new NoTargetException ();

			IStackFrame frame = CurrentFrame;
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
			if (inferior == null)
				throw new NoTargetException ();

			IStackFrame frame = CurrentFrame;
			ILanguageBackend language = (frame.Method != null) ? frame.Method.Language : null;

			return new StepFrame (language, mode);
		}

		public void StepInstruction ()
		{
			if (inferior == null)
				throw new NoTargetException ();

			if (State != TargetState.STOPPED)
				throw new TargetNotStoppedException ();

			inferior.Step (get_simple_step_frame (StepMode.SingleInstruction));
		}

		public void NextInstruction ()
		{
			if (inferior == null)
				throw new NoTargetException ();

			if (State != TargetState.STOPPED)
				throw new TargetNotStoppedException ();

			inferior.Step (get_simple_step_frame (StepMode.NextInstruction));
		}

		public void StepLine ()
		{
			if (inferior == null)
				throw new NoTargetException ();

			if (State != TargetState.STOPPED)
				throw new TargetNotStoppedException ();

			inferior.Step (get_step_frame ());
		}

		public void NextLine ()
		{
			if (inferior == null)
				throw new NoTargetException ();

			if (State != TargetState.STOPPED)
				throw new TargetNotStoppedException ();

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
			if (inferior == null)
				throw new NoTargetException ();

			inferior.Continue ();
		}

		public void Stop ()
		{
			if (inferior == null)
				throw new NoTargetException ();

			inferior.Stop ();
		}

		public void Finish ()
		{
			if (inferior == null)
				throw new NoTargetException ();

			if (State != TargetState.STOPPED)
				throw new TargetNotStoppedException ();

			IStackFrame frame = CurrentFrame;
			if (frame.Method == null)
				throw new NoMethodException ();

			inferior.Step (new StepFrame (
				frame.Method.StartAddress, frame.Method.EndAddress, null, StepMode.Finish));
		}

		public TargetAddress CurrentFrameAddress {
			get {
				if (inferior == null)
					throw new NoTargetException ();

				if (State != TargetState.STOPPED)
					throw new TargetNotStoppedException ();

				return inferior.CurrentFrame;
			}
		}

		public IStackFrame CurrentFrame {
			get {
				if (inferior == null)
					throw new NoTargetException ();

				if (State != TargetState.STOPPED)
					throw new TargetNotStoppedException ();

				return current_frame;
			}
		}

		public IMethod CurrentMethod {
			get {
				if (inferior == null)
					throw new NoTargetException ();

				if (State != TargetState.STOPPED)
					throw new TargetNotStoppedException ();

				return current_method;
			}
		}

		public IStackFrame[] GetBacktrace (int max_frames, bool full_backtrace)
		{
			if (inferior == null)
				throw new NoTargetException ();

			if (State != TargetState.STOPPED)
				throw new TargetNotStoppedException ();

			symtabs.UpdateSymbolTable ();

			TargetAddress[] frames = inferior.GetBacktrace (max_frames, full_backtrace);
			IStackFrame[] retval = new IStackFrame [frames.Length];

			for (int i = 0; i < frames.Length; i++) {
				IMethod method = Lookup (frames [i]);
				if ((method != null) && method.HasSource) {
					ISourceLocation source = method.Source.Lookup (frames [i]);
					retval [i] = new StackFrame (inferior, frames [i], source, method);
				} else
					retval [i] = new StackFrame (inferior, frames [i]);
			}

			return retval;
		}

		public long GetRegister (int register)
		{
			if (inferior == null)
				throw new NoTargetException ();

			return inferior.GetRegister (register);
		}

		public long[] GetRegisters (int[] registers)
		{
			if (inferior == null)
				throw new NoTargetException ();

			return inferior.GetRegisters (registers);
		}

		public IDisassembler Disassembler {
			get {
				if (inferior == null)
					throw new NoTargetException ();

				return inferior.Disassembler;
			}
		}

		public IArchitecture Architecture {
			get {
				if (inferior == null)
					throw new NoTargetException ();

				return inferior.Architecture;
			}
		}

		public ITargetMemoryAccess TargetMemoryAccess {
			get {
				if (inferior == null)
					throw new NoTargetException ();

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

			TargetAddress address = inferior.CurrentFrame;
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
				current_frame = new StackFrame (inferior, address, source, current_method);
			} else
				current_frame = new StackFrame (inferior, address);

			if (FrameChangedEvent != null)
				FrameChangedEvent (current_frame);
		}

		public ISourceFileFactory SourceFileFactory {
			get {
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

		~Debugger ()
		{
			Dispose (false);
		}
	}
}
