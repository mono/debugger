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
		BinaryReader reader;
		int offset;
		ITargetInfo target_info;

		internal TargetReader (byte[] data, ITargetInfo target_info)
		{
			this.reader = new BinaryReader (new MemoryStream (data));
			this.target_info = target_info;
			this.data = data;
			this.offset = 0;
		}

		public long Offset {
			get {
				return reader.BaseStream.Position;
			}

			set {
				reader.BaseStream.Position = value;
			}
		}

		public BinaryReader BinaryReader {
			get {
				return reader;
			}
		}

		public int TargetIntegerSize {
			get {
				return target_info.TargetIntegerSize;
			}
		}

		public int TargetLongIntegerSize {
			get {
				return target_info.TargetLongIntegerSize;
			}
		}

		public int TargetAddressSize {
			get {
				return target_info.TargetAddressSize;
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

		public ITargetLocation ReadAddress ()
		{
			if (TargetAddressSize == 4)
				return new TargetLocation (reader.ReadInt32 ());
			else if (TargetAddressSize == 8)
				return new TargetLocation (reader.ReadInt64 ());
			else
				throw new TargetMemoryException (
					"Unknown target address size " + TargetAddressSize);
		}
	}

	internal class TargetMemoryStream : Stream
	{
		ITargetLocation location;
		ITargetInfo target_info;
		ITargetMemoryAccess memory;

		internal TargetMemoryStream (ITargetMemoryAccess memory, ITargetLocation location,
					     ITargetInfo target_info)
		{
			this.memory = memory;
			this.location = (ITargetLocation) location.Clone ();
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
				return location.Offset;
			}

			set {
				location.Offset = (int) value;
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
				ref_point = location.Offset;
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

                        location.Offset = (int) (ref_point + offset);

                        return location.Offset;
                }

		public override void Flush ()
		{
		}

		public override int Read (byte[] buffer, int offset, int count)
		{
			try {
				byte[] retval = memory.ReadBuffer (location, count);
				retval.CopyTo (buffer, offset);
			} catch (Exception e) {
				throw new IOException ("Cannot read target memory", e);
			}

			location.Offset += count;
			return count;
		}

		public override void Write (byte[] buffer, int offset, int count)
		{
			try {
				if (offset != 0) {
					byte[] temp = new byte [count];
					Array.Copy (buffer, offset, temp, 0, count);
					memory.WriteBuffer (location, temp, count);
				} else
					memory.WriteBuffer (location, buffer, count);
			} catch (Exception e) {
				throw new IOException ("Cannot read target memory", e);
			}

			location.Offset += count;
		}
	}

	internal class StackFrame : IStackFrame
	{
		public readonly ISourceLocation SourceLocation = null;
		public readonly ITargetLocation TargetLocation = null;
		public readonly IInferior Inferior;

		IMethod method;

		public StackFrame (IInferior inferior, ITargetLocation location,
				   ISourceLocation source, IMethod method)
			: this (inferior, location)
		{
			this.SourceLocation = source;
			this.method = method;
		}

		public StackFrame (IInferior inferior, ITargetLocation location)
		{
			Inferior = inferior;
			TargetLocation = location;
		}

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

		IMethod IStackFrame.Method {
			get {
				return method;
			}
		}

		public override string ToString ()
		{
			StringBuilder builder = new StringBuilder ();

			if (SourceLocation != null) {
				builder.Append (SourceLocation);
				builder.Append (" at ");
			}
			builder.Append (TargetLocation);

			return builder.ToString ();
		}
	}

	internal class StepFrame : IStepFrame
	{
		ITargetLocation start, end;

		internal StepFrame (ITargetLocation start, ITargetLocation end)
		{
			this.start = start;
			this.end = end;
		}

		public ITargetLocation Start {
			get {
				return start;
			}
		}

		public ITargetLocation End {
			get {
				return end;
			}
		}

		public override string ToString ()
		{
			return String.Format ("StepFrame ({0:x},{1:x})", Start, End);
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

		Assembly application;
		Inferior inferior;
		MonoSymbolTableCollection mono_symtab;

		readonly uint target_address_size;
		readonly uint target_integer_size;
		readonly uint target_long_integer_size;

		string[] argv;
		string[] envp;
		string working_directory;

		bool initialized;

		public Debugger (string application, string[] arguments)
			: this (application, arguments, new SourceFileFactory ())
		{ }

		public Debugger (string application, string[] arguments, ISourceFileFactory source_factory)
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
			this.application = Assembly.LoadFrom (application);

			MethodInfo main = this.application.EntryPoint;
			string main_name = main.DeclaringType + ":" + main.Name;

			string[] argv = { Path_Mono, "--break", main_name, "--debug=mono",
					  "--noinline", "--nols", "--debug-args", "internal_mono_debugger",
					  application };
			string[] envp = { "PATH=" + Environment_Path, "LD_BIND_NOW=yes" };
			this.argv = argv;
			this.envp = envp;
			this.working_directory = ".";
		}

		public Debugger (string[] argv)
			: this (argv, new SourceFileFactory ())
		{ }

		public Debugger (string[] argv, ISourceFileFactory source_factory)
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

			string[] envp = { "PATH=" + Environment_Path };
			this.argv = argv;
			this.envp = envp;
			this.working_directory = ".";
		}

		//
		// IInferior
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
					StateChanged (State);
			}
		}

		void target_state_changed (TargetState new_state)
		{
			if (new_state == TargetState.STOPPED)
				frame_changed ();

			if (StateChanged != null)
				StateChanged (new_state);
		}

		public event TargetOutputHandler TargetOutput;
		public event TargetOutputHandler TargetError;
		public event StateChangedHandler StateChanged;

		//
		// IDebuggerBackend
		//

		public event StackFrameHandler FrameChangedEvent;
		public event StackFramesInvalidHandler FramesInvalidEvent;

		public IInferior Inferior {
			get {
				return inferior;
			}
		}

		void child_exited ()
		{
			inferior.Dispose ();
			inferior = null;
			mono_symtab = null;
			initialized = false;
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
				throw new TargetException ("Debugger already has an inferior.");

			bool native = application == null;

			inferior = new Inferior (working_directory, argv, envp, native, source_factory);
			inferior.ChildExited += new ChildExitedHandler (child_exited);
			inferior.TargetOutput += new TargetOutputHandler (inferior_output);
			inferior.TargetError += new TargetOutputHandler (inferior_errors);
			inferior.StateChanged += new StateChangedHandler (target_state_changed);

			if (!native)
				mono_symtab = new MonoSymbolTableCollection (inferior);
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
			if (frame.SourceLocation == null)
				return null;

			int offset = frame.SourceLocation.SourceOffset;
			int range = frame.SourceLocation.SourceRange;

			ITargetLocation start = new TargetLocation (frame.TargetLocation.Address - offset);
			ITargetLocation end = (ITargetLocation) start.Clone ();
			end.Offset += range;

			return new StepFrame (start, end);
		}

		public void StepLine ()
		{
			inferior.Step (get_step_frame ());
		}

		public void NextLine ()
		{
			IStepFrame frame = get_step_frame ();
			if (frame == null) {
				inferior.Next ();
				return;
			}

			Console.WriteLine ("RUNNING UNTIL: {1} {0:x}", frame.End.Address, frame.End);

			inferior.Continue (frame.End);
		}

		public IStackFrame CurrentFrame {
			get {
				if (inferior == null)
					throw new NoTargetException ();

				ITargetLocation location = inferior.CurrentFrame;

				if (mono_symtab != null)
					mono_symtab.UpdateSymbolTables ();

				IMethod method = Lookup (location);
				if ((method != null) && method.HasSource) {
					ISourceLocation source = method.Source.Lookup (location);
					return new StackFrame (inferior, location, source, method);
				}

				return new StackFrame (inferior, location);
			}
		}

		public IMethod Lookup (ITargetLocation address)
		{
			if (inferior == null)
				return null;

			if (inferior.SymbolTables != null) {
				IMethod method = inferior.SymbolTables.Lookup (address);
				if (method != null)
					return method;
			}

			if (mono_symtab == null)
				return null;

			return mono_symtab.Lookup (address);
		}


		void frame_changed ()
		{
			if (FrameChangedEvent != null)
				FrameChangedEvent (CurrentFrame);
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
