using GLib;
using System;
using System.IO;
using System.Text;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.CSharp.Debugger;

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

	internal class StackFrame : IStackFrame
	{
		public readonly ISourceLocation SourceLocation = null;
		public readonly ITargetLocation TargetLocation = null;
		public readonly ISymbolHandle SymbolHandle = null;

		public StackFrame (ITargetLocation location)
		{
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

	public class Debugger : IDebuggerBackend, IDisposable
	{
		public readonly string Path_Mono	= "mono";
		public readonly string Environment_Path	= "/usr/bin";

		ISourceFileFactory source_file_factory;

		Assembly application;
		Inferior inferior;

		readonly uint target_address_size;
		readonly uint target_integer_size;
		readonly uint target_long_integer_size;

		string[] argv;
		string[] envp;
		string working_directory;

		bool initialized;
		bool symtabs_read;

		MonoDebuggerInfo mono_debugger_info;

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

			this.source_file_factory = source_factory;
			this.application = Assembly.LoadFrom (application);

			MethodInfo main = this.application.EntryPoint;
			string main_name = main.DeclaringType + ":" + main.Name;

			string[] argv = { Path_Mono, "--break", main_name, "--debug=mono",
					  "--noinline", "--nols", "--debug-args", "internal_mono_debugger",
					  application };
			string[] envp = { "PATH=" + Environment_Path };
			this.argv = argv;
			this.envp = envp;
			this.working_directory = ".";
		}

		//
		// IInferior
		//

		TargetState target_state = TargetState.NO_TARGET;
		public TargetState State {
			get {
				return target_state;
			}
		}

		void change_target_state (TargetState new_state)
		{
			if (new_state == target_state)
				return;

			target_state = new_state;

			if (new_state == TargetState.STOPPED)
				IDebuggerBackend.Frame ();

			if (StateChanged != null)
				StateChanged (target_state);
		}

		public void Continue ()
		{
			if (inferior == null)
				throw new NoTargetException ();

			inferior.Continue ();
			change_target_state (TargetState.RUNNING);
		}

		public void Shutdown ()
		{
			if (inferior != null) {
				inferior.Shutdown ();
				inferior.Dispose ();
				inferior = null;
			}
		}

		public void Kill ()
		{
			if (inferior != null) {
				inferior.Kill ();
				inferior.Dispose ();
				inferior = null;
			}
		}

		public ITargetLocation Frame ()
		{
			if (inferior == null)
				throw new NoTargetException ();

			return inferior.Frame ();
		}

		public void Step ()
		{
			if (inferior == null)
				throw new NoTargetException ();

			inferior.Step ();
			change_target_state (TargetState.RUNNING);
		}
		
		public void Next ()
		{
			if (inferior == null)
				throw new NoTargetException ();

			throw new NotSupportedException ();
		}

		public event TargetOutputHandler TargetOutput;
		public event TargetOutputHandler TargetError;
		public event StateChangedHandler StateChanged;

		//
		// IDebuggerBackend
		//

		public event StackFrameHandler CurrentFrameEvent;
		public event StackFramesInvalidHandler FramesInvalidEvent;

		void child_exited ()
		{
			inferior.Dispose ();
			inferior = null;
			initialized = false;
			symtabs_read = false;
			mono_debugger_info = null;
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

		void child_message (ChildMessage message, int args)
		{
			switch (message) {
			case ChildMessage.CHILD_STOPPED:
				if (!initialized) {
					Continue ();
					initialized = true;
					break;
				} else if (!symtabs_read) {
					symtabs_read = true;
					mono_debugger_info = inferior.MonoDebuggerInfo;
				}
				change_target_state (TargetState.STOPPED);
				break;

			case ChildMessage.CHILD_EXITED:
			case ChildMessage.CHILD_SIGNALED:
				change_target_state (TargetState.EXITED);
				break;

			default:
				Console.WriteLine ("CHILD MESSAGE: {0} {1}", message, args);
				break;
			}
		}

		public void Run ()
		{
			if (inferior != null)
				throw new TargetException ("Debugger already has an inferior.");

			inferior = new Inferior (working_directory, argv, envp);
			inferior.ChildExited += new ChildExitedHandler (child_exited);
			inferior.ChildMessage += new ChildMessageHandler (child_message);
			inferior.TargetOutput += new TargetOutputHandler (inferior_output);
			inferior.TargetError += new TargetOutputHandler (inferior_errors);

			change_target_state (TargetState.STOPPED);
		}

		public void Quit ()
		{
			Shutdown ();
		}

		void IDebuggerBackend.Frame ()
		{
			ITargetLocation location = IInferior.Frame ();

			if (CurrentFrameEvent != null)
				CurrentFrameEvent (new StackFrame (location));
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
				throw new NotImplementedException ();
			}
		}

		uint IDebuggerBackend.TargetIntegerSize {
			get {
				throw new NotImplementedException ();
			}
		}

		uint IDebuggerBackend.TargetLongIntegerSize {
			get {
				throw new NotImplementedException ();
			}
		}

		public byte ReadByte (long address)
		{
			throw new NotImplementedException ();
		}

		public uint ReadInteger (long address)
		{
			throw new NotImplementedException ();
		}

		public int ReadSignedInteger (long address)
		{
			throw new NotImplementedException ();
		}

		public long ReadAddress (long address)
		{
			throw new NotImplementedException ();
		}

		public long ReadLongInteger (long address)
		{
			throw new NotImplementedException ();
		}

		public string ReadString (long address)
		{
			throw new NotImplementedException ();
		}

		public int ReadIntegerRegister (string name)
		{
			throw new NotImplementedException ();
		}

		public IBreakPoint AddBreakPoint (ITargetLocation location)
		{
			throw new NotImplementedException ();
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
					Kill ();
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
