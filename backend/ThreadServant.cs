using System;
using System.IO;
using System.Text;
using ST = System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Backends
{
	internal abstract class ThreadServant : TargetAccess
	{
		protected ThreadServant (ThreadManager manager, ProcessServant process)
		{
			this.manager = manager;
			this.process = process;

			this.id = manager.NextThreadID;

			tgroup = process.Session.CreateThreadGroup ("@" + ID);
			tgroup.AddThread (ID);

			thread = process.Debugger.Client.CreateThread (this, ID);
		}

		protected readonly int id;
		protected readonly Thread thread;
		protected readonly ProcessServant process;
		protected readonly ThreadManager manager;
		protected readonly ThreadGroup tgroup;

		bool is_daemon;

		protected internal Language NativeLanguage {
			get { return process.BfdContainer.NativeLanguage; }
		}

		public override string ToString ()
		{
			return Name;
		}

		public int ID {
			get { return id; }
		}

		public string Name {
			get {
				if (IsDaemon)
					return String.Format ("Daemon thread @{0}", id);
				else
					return String.Format ("Thread @{0}", id);
			}
		}

		public Thread Client {
			get { return thread; }
		}

		public abstract int PID {
			get;
		}

		public abstract long TID {
			get;
		}

		public abstract TargetAddress LMFAddress {
			get;
		}

		internal ProcessServant Process {
			get { return process; }
		}

		public bool IsDaemon {
			get { return is_daemon; }
		}

		public ThreadGroup ThreadGroup {
			get { return tgroup; }
		}

		internal void SetDaemon ()
		{
			is_daemon = true;
			thread.SetThreadFlags (thread.ThreadFlags | Thread.Flags.Daemon | Thread.Flags.Immutable);
		}

		public abstract TargetEventArgs LastTargetEvent {
			get;
		}

		public abstract TargetMemoryArea[] GetMemoryMaps ();

		public abstract Method Lookup (TargetAddress address);

		public abstract Symbol SimpleLookup (TargetAddress address, bool exact_match);

		// <summary>
		//   The current method  May only be used when the engine is stopped
		//   (State == TargetState.STOPPED).  The single stepping engine
		//   automatically computes the current frame and current method each time
		//   a stepping operation is completed.  This ensures that we do not
		//   unnecessarily compute this several times if more than one client
		//   accesses this property.
		// </summary>
		public abstract Method CurrentMethod {
			get;
		}

		// <summary>
		//   Step one machine instruction, but don't step into trampolines.
		// </summary>
		public abstract void StepInstruction (CommandResult result);

		// <summary>
		//   Step one machine instruction, always step into method calls.
		// </summary>
		public abstract void StepNativeInstruction (CommandResult result);

		// <summary>
		//   Step one machine instruction, but step over method calls.
		// </summary>
		public abstract void NextInstruction (CommandResult result);

		// <summary>
		//   Step one source line.
		// </summary>
		public abstract void StepLine (CommandResult result);

		// <summary>
		//   Step one source line, but step over method calls.
		// </summary>
		public abstract void NextLine (CommandResult result);

		// <summary>
		//   Continue until leaving the current method.
		// </summary>
		public abstract void Finish (bool native, CommandResult result);

		public abstract void Continue (TargetAddress until, CommandResult result);

		public abstract void Background (TargetAddress until, CommandResult result);

		public abstract void Kill ();

		public abstract void Detach ();

		internal abstract void DetachThread ();

		public abstract void Stop ();

		internal abstract object Invoke (TargetAccessDelegate func, object data);

		// <summary>
		//   Insert a breakpoint at address @address.
		//
		//   Returns a number which may be passed to RemoveBreakpoint() to remove
		//   the breakpoint.
		// </summary>
		internal abstract override void InsertBreakpoint (BreakpointHandle handle,
								  TargetAddress address, int domain);

		// <summary>
		//   Remove breakpoint @index.  @index is the breakpoint number which has
		//   been returned by InsertBreakpoint().
		// </summary>
		internal abstract override void RemoveBreakpoint (BreakpointHandle handle);

		// <summary>
		//   Add an event handler.
		//
		//   Returns a number which may be passed to RemoveEventHandler() to remove
		//   the event handler.
		// </summary>
		public abstract int AddEventHandler (Event handle);

		// <summary>
		//   Remove event handler @index.  @index is the event handler number which has
		//   been returned by AddEventHandler().
		// </summary>
		public abstract void RemoveEventHandler (int index);

		internal abstract void AcquireThreadLock ();

		internal abstract void ReleaseThreadLock ();

		internal abstract void ReleaseThreadLockDone ();

		public abstract string PrintObject (Style style, TargetObject obj, DisplayFormat format);

		public abstract string PrintType (Style style, TargetType type);

		internal abstract Registers GetCallbackFrame (TargetAddress stack_pointer,
							      bool exact_match);

		public abstract void RuntimeInvoke (TargetFunctionType function,
						    TargetClassObject object_argument,
						    TargetObject[] param_objects,
						    bool is_virtual, bool debug,
						    RuntimeInvokeResult result);

		public abstract CommandResult CallMethod (TargetAddress method, long arg1,
							  long arg2);

		public abstract CommandResult CallMethod (TargetAddress method, long arg1,
							  long arg2, string string_arg);

		public abstract CommandResult CallMethod (TargetAddress method, TargetAddress arg);

		public abstract CommandResult Return (bool run_finally);

		public abstract CommandResult AbortInvocation ();

		public string PrintRegisters (StackFrame frame)
		{
			return Architecture.PrintRegisters (frame);
		}

		public abstract bool IsAlive {
			get;
		}

		public abstract bool CanRun {
			get;
		}

		public abstract bool CanStep {
			get;
		}

		public abstract bool IsStopped {
			get;
		}

		public override int TargetAddressSize {
			get { return TargetMemoryInfo.TargetAddressSize; }
		}

		public override int TargetIntegerSize {
			get { return TargetMemoryInfo.TargetIntegerSize; }
		}

		public override int TargetLongIntegerSize {
			get { return TargetMemoryInfo.TargetLongIntegerSize; }
		}

		public override bool IsBigEndian {
			get { return TargetMemoryInfo.IsBigEndian; }
		}

		public override AddressDomain AddressDomain {
			get { return TargetMemoryInfo.AddressDomain; }
		}

#region IDisposable implementation
		private bool disposed = false;

		protected void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("ThreadServant");
		}

		protected virtual void DoDispose ()
		{
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (disposed)
				return;

			disposed = true;

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				DoDispose ();
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}
#endregion

		~ThreadServant ()
		{
			Dispose (false);
		}
	}
}
