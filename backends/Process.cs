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
	public delegate void ProcessExitedHandler (Process process);

	public abstract class Process : IProcess, IDisposable
	{
		DebuggerBackend backend;
		ProcessStart start;
		BfdContainer bfd_container;

		protected SingleSteppingEngine sse;
		DaemonThreadRunner runner;
		IProcess iprocess;
		bool is_daemon;
		int pid, id;

		static int next_id = 0;

		internal enum ProcessType
		{
			Normal,
			CoreFile,
			Daemon,
			ManagedWrapper,
			CommandProcess
		}

		protected Process (Inferior inferior)
		{
			this.backend = inferior.DebuggerBackend;
			this.start = inferior.ProcessStart;
			this.bfd_container = inferior.BfdContainer;
			this.pid = inferior.PID;

			inferior.TargetOutput += new TargetOutputHandler (inferior_output);
			inferior.DebuggerOutput += new DebuggerOutputHandler (debugger_output);
			inferior.DebuggerError += new DebuggerErrorHandler (debugger_error);

			id = ++next_id;
			sse = new SingleSteppingEngine (backend, this, inferior, start.IsNative);

			sse.TargetEvent += new TargetEventHandler (target_event);
			sse.TargetExitedEvent += new TargetExitedHandler (child_exited);
			iprocess = sse;
		}

		protected void SetDaemonThreadRunner (DaemonThreadRunner runner)
		{
			this.runner = runner;
			runner.TargetExited += new TargetExitedHandler (child_exited);
			iprocess = runner.SingleSteppingEngine;
			is_daemon = true;
		}

		public DebuggerBackend DebuggerBackend {
			get {
				check_disposed ();
				return backend;
			}
		}

		public SingleSteppingEngine SingleSteppingEngine {
			get {
				check_disposed ();
				return sse;
			}
		}

		public ITargetMemoryInfo TargetMemoryInfo {
			get {
				check_iprocess ();
				return iprocess.TargetMemoryInfo;
			}
		}

		public ITargetMemoryAccess TargetMemoryAccess {
			get {
				check_iprocess ();
				return iprocess.TargetMemoryAccess;
			}
		}

		public ITargetAccess TargetAccess {
			get {
				check_iprocess ();
				return iprocess.TargetAccess;
			}
		}

		public IDisassembler Disassembler {
			get {
				check_iprocess ();
				return iprocess.Disassembler;
			}
		}

		public IArchitecture Architecture {
			get {
				check_iprocess ();
				return iprocess.Architecture;
			}
		}

		//
		// ITargetNotification
		//

		public int ID {
			get {
				return id;
			}
		}

		public int PID {
			get{
				return pid;
			}
		}

		public TargetState State {
			get {
				if (iprocess != null)
					return iprocess.State;
				else
					return TargetState.NO_TARGET;
			}
		}

		public bool IsDaemon {
			get {
				return is_daemon;
			}
		}

		void target_event (object sender, TargetEventArgs args)
		{
			if ((pid == 0) && (sse != null))
				pid = sse.PID;
			OnTargetEvent (args);
		}

		protected virtual void OnTargetEvent (TargetEventArgs args)
		{
			if (TargetEvent != null)
				TargetEvent (this, args);
		}

		public event TargetOutputHandler TargetOutput;
		public event DebuggerOutputHandler DebuggerOutput;
		public event DebuggerErrorHandler DebuggerError;

		public event TargetEventHandler TargetEvent;
		public event TargetExitedHandler TargetExited;
		public event ProcessExitedHandler ProcessExitedEvent;

		void inferior_output (bool is_stderr, string line)
		{
			if (TargetOutput != null)
				TargetOutput (is_stderr, line);
		}

		void debugger_output (string line)
		{
			if (DebuggerOutput != null)
				DebuggerOutput (line);
		}

		void debugger_error (object sender, string message, Exception e)
		{
			if (DebuggerError != null)
				DebuggerError (this, message, e);
		}

		// <summary>
		//   If true, we have a target.
		// </summary>
		public bool HasTarget {
			get { return iprocess != null; }
		}

		// <summary>
		//   If true, we have a target which can be executed (ie. it's not a core file).
		// </summary>
		public bool CanRun {
			get { return HasTarget && sse != null; }
		}

		// <summary>
		//   If true, we have a target which can be executed and it is currently stopped
		//   so that we can issue a step command.
		// </summary>
		public bool CanStep {
			get { return CanRun && sse.State == TargetState.STOPPED; }
		}

		// <summary>
		//   If true, the target is currently stopped and thus its memory/registers can
		//   be read/writtern.
		// </summary>
		public bool IsStopped {
			get { return State == TargetState.STOPPED || State == TargetState.CORE_FILE; }
		}

		public bool StepInstruction (bool synchronous)
		{
			check_sse ();
			return sse.StepInstruction (synchronous);
		}

		public bool StepNativeInstruction (bool synchronous)
		{
			check_sse ();
			return sse.StepNativeInstruction (synchronous);
		}

		public bool NextInstruction (bool synchronous)
		{
			check_sse ();
			return sse.NextInstruction (synchronous);
		}

		public bool StepLine (bool synchronous)
		{
			check_sse ();
			return sse.StepLine (synchronous);
		}

		public bool NextLine (bool synchronous)
		{
			check_sse ();
			return sse.NextLine (synchronous);
		}

		public bool Continue (bool synchronous)
		{
			check_sse ();
			return sse.Continue (false, synchronous);
		}

		public bool Continue (bool in_background, bool synchronous)
		{
			check_sse ();
			return sse.Continue (in_background, synchronous);
		}

		public bool Continue (TargetAddress until, bool synchronous)
		{
			check_sse ();
			return sse.Continue (until, synchronous);
		}

		public void Stop ()
		{
			check_disposed ();
			if (sse != null)
				sse.Stop ();
		}

		public bool Finish (bool synchronous)
		{
			check_sse ();
			return sse.Finish (synchronous);
		}

		public void Kill ()
		{
			if (disposed)
				return;
			if (sse != null) {
				sse.Kill ();
				sse = null;
			}
			child_exited ();
			Dispose ();
		}

		public TargetAddress CurrentFrameAddress {
			get {
				check_iprocess ();
				return iprocess.CurrentFrameAddress;
			}
		}

		public StackFrame CurrentFrame {
			get {
				check_iprocess ();
				return iprocess.CurrentFrame;
			}
		}

		public Backtrace GetBacktrace ()
		{
			check_iprocess ();
			return iprocess.GetBacktrace ();
		}

		public Backtrace GetBacktrace (int max_frames)
		{
			check_iprocess ();
			return iprocess.GetBacktrace (max_frames);
		}

		public Register[] GetRegisters ()
		{
			check_iprocess ();
			return iprocess.GetRegisters ();
		}

		public virtual long GetRegister (int index)
		{
			check_iprocess ();
			return iprocess.TargetAccess.GetRegister (index);
		}

		public virtual long[] GetRegisters (int[] indices)
		{
			long[] retval = new long [indices.Length];
			for (int i = 0; i < indices.Length; i++)
				retval [i] = GetRegister (indices [i]);
			return retval;
		}

		public void SetRegister (int register, long value)
		{
			check_sse ();
			sse.SetRegister (register, value);
		}

		public void SetRegisters (int[] registers, long[] values)
		{
			check_sse ();
			sse.SetRegisters (registers, values);
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			check_iprocess ();
			return iprocess.GetMemoryMaps ();
		}

		public ProcessStart ProcessStart {
			get { return start; }
		}

		void child_exited ()
		{
			if (TargetExited != null)
				TargetExited ();
			if (ProcessExitedEvent != null)
				ProcessExitedEvent (this);
			Dispose ();
		}

		void check_iprocess ()
		{
			check_disposed ();
			if (iprocess == null)
				throw new NoTargetException ();
		}

		void check_sse ()
		{
			check_disposed ();
			if (sse == null)
				throw new NoTargetException ();
		}

		public void Run ()
		{
			if (runner != null)
				runner.Run ();
			else if (sse != null)
				sse.Run ();
		}

		internal abstract void RunInferior ();

		public override string ToString ()
		{
			return String.Format ("Process ({0}:{1}:{2})", is_daemon, id, pid);
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Process");
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (disposed)
				return;

			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				if (iprocess != null)
					iprocess.Dispose ();
				iprocess = null;
				if (runner != null)
					runner.Dispose ();
				runner = null;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~Process ()
		{
			Dispose (false);
		}
	}
}
