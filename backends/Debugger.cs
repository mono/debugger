using GLib;
using System;
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

			throw new NotSupportedException ();
		}

		public void Step ()
		{
			if (inferior == null)
				throw new NoTargetException ();
		}
		
		public void Next ()
		{
			if (inferior == null)
				throw new NoTargetException ();
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
			IInferior.Frame ();
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
