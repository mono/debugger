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

using Mono.Debugger.Backend;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	[Serializable]
	internal delegate object TargetAccessDelegate (Thread target, object user_data);

	public class Thread : DebuggerMarshalByRefObject, IOperationHost
	{
		[Flags]
		public enum Flags {
			None		= 0x0000,
			Daemon		= 0x0001,
			Immutable	= 0x0002,
			Background	= 0x0004,
			AutoRun		= 0x0008,
			StopOnExit	= 0x0010
		}

		internal Thread (ThreadServant servant, int id)
		{
			this.id = id;
			this.servant = servant;
			this.flags = Flags.AutoRun;
		}

		int id;
		Flags flags;
		ThreadServant servant;

		public ST.WaitHandle WaitHandle {
			get { return servant.WaitHandle; }
		}

		protected internal Language NativeLanguage {
			get {
				check_servant ();
				return servant.NativeLanguage;
			}
		}

		internal TargetAddress LMFAddress {
			get {
				check_servant ();
				return servant.LMFAddress;
			}
		}

		public override string ToString ()
		{
			return Name;
		}

		// <summary>
		//   The single-stepping engine's target state.  This will be
		//   TargetState.RUNNING while the engine is stepping.
		// </summary>
		public TargetState State {
			get {
				if (servant == null)
					return TargetState.NoTarget;
				else
					return servant.State;
			}
		}

		public TargetEventArgs GetLastTargetEvent ()
		{
			check_servant ();
			lock (this) {
				TargetEventArgs args = servant.LastTargetEvent;
				if (args != null) {
					flags &= ~Flags.Background;
					return args;
				}
			}
			return null;
		}

		public bool IsRunning {
			get { return (servant != null) && !servant.IsStopped; }
		}

		public bool IsAlive {
			get { return (servant != null) && servant.IsAlive; }
		}

		public int ID {
			get { return id; }
		}

		public Flags ThreadFlags {
			get { return flags; }
			set { flags = value; }
		}

		public string Name {
			get {
				if ((flags & Flags.Daemon) != 0)
					return String.Format ("Daemon thread @{0}", id);
				else
					return String.Format ("Thread @{0}", id);
			}
		}

		public int PID {
			get {
				check_servant ();
				return servant.PID;
			}
		}

		public long TID {
			get {
				check_servant ();
				return servant.TID;
			}
		}

		internal Architecture Architecture {
			get {
				check_servant ();
				return servant.Architecture;
			}
		}

		internal ProcessServant ProcessServant {
			get {
				check_servant ();
				return servant.ProcessServant;
			}
		}

		public Process Process {
			get {
				check_servant ();
				return ProcessServant.Client;
			}
		}

		internal ThreadManager ThreadManager {
			get {
				check_servant ();
				return servant.ThreadManager;
			}
		}

		internal ThreadServant ThreadServant {
			get {
				check_servant ();
				return servant;
			}
		}

		public ThreadGroup ThreadGroup {
			get {
				check_servant ();
				return servant.ThreadGroup;
			}
		}

		void check_servant ()
		{
			if (servant == null)
				throw new TargetException (TargetError.NoTarget);
		}

		void check_alive ()
		{
			if ((servant == null) || !servant.IsAlive)
				throw new TargetException (TargetError.NoTarget);
		}

		// <summary>
		//   The current stack frame.  May only be used when the engine is stopped
		//   (State == TargetState.STOPPED).  The single stepping engine
		//   automatically computes the current frame and current method each time
		//   a stepping operation is completed.  This ensures that we do not
		//   unnecessarily compute this several times if more than one client
		//   accesses this property.
		// </summary>
		public StackFrame CurrentFrame {
			get {
				check_servant ();
				return servant.CurrentFrame;
			}
		}

		public TargetAddress CurrentFrameAddress {
			get {
				check_servant ();
				return servant.CurrentFrameAddress;
			}
		}

		// <summary>
		//   The current stack frame.  May only be used when the engine is stopped
		//   (State == TargetState.STOPPED).  The backtrace is generated on
		//   demand, when this function is called.  However, the single stepping
		//   engine will compute this only once each time a stepping operation is
		//   completed.  This means that if you call this function several times
		//   without doing any stepping operations in the meantime, you'll always
		//   get the same backtrace.
		// </summary>
		public Backtrace GetBacktrace (Backtrace.Mode mode, int max_frames)
		{
			check_servant ();
			return servant.GetBacktrace (mode, max_frames);
		}

		public Backtrace GetBacktrace (int max_frames)
		{
			return GetBacktrace (Backtrace.Mode.Default, max_frames);
		}

		public Backtrace GetBacktrace ()
		{
			check_servant ();
			Backtrace bt = servant.CurrentBacktrace;
			if (bt != null)
				return bt;

			return GetBacktrace (Backtrace.Mode.Default, -1);
		}

		public Backtrace CurrentBacktrace {
			get {
				check_servant ();
				return servant.CurrentBacktrace;
			}
		}

		public Registers GetRegisters ()
		{
			check_servant ();
			return servant.GetRegisters ();
		}

		public void SetRegisters (Registers registers)
		{
			check_alive ();
			servant.SetRegisters (registers);
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			check_servant ();
			return servant.GetMemoryMaps ();
		}

		public Method Lookup (TargetAddress address)
		{
			check_servant ();
			return servant.Lookup (address);
		}

		public Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			check_servant ();
			return servant.SimpleLookup (address, exact_match);
		}

		// <summary>
		//   The current method  May only be used when the engine is stopped
		//   (State == TargetState.STOPPED).  The single stepping engine
		//   automatically computes the current frame and current method each time
		//   a stepping operation is completed.  This ensures that we do not
		//   unnecessarily compute this several times if more than one client
		//   accesses this property.
		// </summary>
		public Method CurrentMethod {
			get {
				check_servant ();
				return servant.CurrentMethod;
			}
		}

		public CommandResult Step (ThreadingModel model, StepMode mode)
		{
			lock (this) {
				check_alive ();
				return servant.Step (model, mode, null);
			}
		}

		public CommandResult Step (ThreadingModel model, StepMode mode, StepFrame frame)
		{
			lock (this) {
				check_alive ();
				return servant.Step (model, mode, frame);
			}
		}

		protected ThreadCommandResult Old_Step (StepMode mode)
		{
			return Old_Step (mode, null);
		}

		protected ThreadCommandResult Old_Step (StepMode mode, StepFrame frame)
		{
			lock (this) {
				check_alive ();
				return servant.Old_Step (mode, frame);
			}
		}

		// <summary>
		//   Step one machine instruction, but don't step into trampolines.
		// </summary>
		[Obsolete("Use Step (StepMode.SingleInstruction)")]
		public ThreadCommandResult StepInstruction ()
		{
			return Old_Step (StepMode.SingleInstruction);
		}

		// <summary>
		//   Step one machine instruction, always step into method calls.
		// </summary>
		[Obsolete("Use Step (StepMode.NativeInstruction)")]
		public ThreadCommandResult StepNativeInstruction ()
		{
			return Old_Step (StepMode.NativeInstruction);
		}

		// <summary>
		//   Step one machine instruction, but step over method calls.
		// </summary>
		[Obsolete("Use Step (StepMode.NextInstruction)")]
		public ThreadCommandResult NextInstruction ()
		{
			return Old_Step (StepMode.NextInstruction);
		}

		// <summary>
		//   Step one source line.
		// </summary>
		[Obsolete("Use Step (StepMode.SourceLine)")]
		public ThreadCommandResult StepLine ()
		{
			return Old_Step (StepMode.SourceLine);
		}

		// <summary>
		//   Step one source line, but step over method calls.
		// </summary>
		[Obsolete("Use Step (StepMode.NextLine)")]
		public ThreadCommandResult NextLine ()
		{
			return Old_Step (StepMode.NextLine);
		}

		// <summary>
		//   Continue until leaving the current method.
		// </summary>
		public ThreadCommandResult Finish (bool native)
		{
			lock (this) {
				check_alive ();

				if (!native) {
					if (CurrentMethod == null)
						throw new TargetException (TargetError.NoMethod);

					StepFrame step_frame = new StepFrame (
						null, StepMode.Finish, null,
						CurrentMethod.StartAddress, CurrentMethod.EndAddress);

					return Old_Step (StepMode.Finish, step_frame);
				} else {
					StepFrame step_frame = new StepFrame (
						null, StepMode.FinishNative,
						CurrentFrame.StackPointer);

					return Old_Step (StepMode.FinishNative, step_frame);
				}
			}
		}

		[Obsolete("Use Step (StepMode.Run)")]
		public ThreadCommandResult Continue ()
		{
			return Old_Step (StepMode.Run);
		}

		[Obsolete("Use the new Step() API")]
		public ThreadCommandResult Continue (TargetAddress until)
		{
			return Old_Step (StepMode.Run, new StepFrame (NativeLanguage, StepMode.Run, until));
		}

		[Obsolete("Background() and Continue() are the same")]
		public ThreadCommandResult Background ()
		{
			return (ThreadCommandResult) Step (ThreadingModel.Single, StepMode.Run, null);
		}

		[Obsolete("Background() and Continue() are the same")]
		public ThreadCommandResult Background (TargetAddress until)
		{
			return Continue (until);
		}

		internal void Kill ()
		{
			if (servant != null)
				servant.Kill ();
			Dispose ();
		}

		internal void Detach ()
		{
			if (servant != null)
				servant.Detach ();
			Dispose ();
		}

		public void Stop ()
		{
			check_alive ();
			servant.Stop ();
		}

		public void AutoStop ()
		{
			check_alive ();
			servant.Stop ();
		}

		public ThreadCommandResult GetWaitHandle ()
		{
			return new ThreadCommandResult (this);
		}

		// <summary>
		//   Insert a breakpoint at address @address.
		//
		//   Returns a number which may be passed to RemoveBreakpoint() to remove
		//   the breakpoint.
		// </summary>
		internal void InsertBreakpoint (BreakpointHandle handle,
						TargetAddress address, int domain)
		{
			check_alive ();
			servant.InsertBreakpoint (handle, address, domain);
		}

		// <summary>
		//   Remove breakpoint @index.  @index is the breakpoint number which has
		//   been returned by InsertBreakpoint().
		// </summary>
		internal void RemoveBreakpoint (BreakpointHandle handle)
		{
			check_disposed ();
			if (servant != null)
				servant.RemoveBreakpoint (handle);
		}

		public string PrintObject (Style style, TargetObject obj, DisplayFormat format)
		{
			check_alive ();
			return servant.PrintObject (style, obj, format);
		}

		public string PrintType (Style style, TargetType type)
		{
			check_alive ();
			return servant.PrintType (style, type);
		}

#region User Threads

		void IOperationHost.OperationCompleted (SingleSteppingEngine sse, TargetEventArgs args, ThreadingModel model)
		{ }

		void IOperationHost.SendResult (SingleSteppingEngine sse, TargetEventArgs args)
		{
			sse.Process.Debugger.SendTargetEvent (sse, args);
		}

		void IOperationHost.Abort ()
		{
			Stop ();
		}

#endregion

		//
		// Disassembling.
		//

		public int GetInstructionSize (TargetAddress address)
		{
			check_alive ();
			return servant.GetInstructionSize (address);
		}

		public AssemblerLine DisassembleInstruction (Method method, TargetAddress address)
		{
			check_alive ();
			return servant.DisassembleInstruction (method, address);
		}

		public AssemblerMethod DisassembleMethod (Method method)
		{
			check_alive ();
			return servant.DisassembleMethod (method);
		}

		[Obsolete]
		public RuntimeInvokeResult RuntimeInvoke (TargetFunctionType function,
							  TargetStructObject object_argument,
							  TargetObject[] param_objects,
							  bool is_virtual, bool debug)
		{
			RuntimeInvokeFlags flags = RuntimeInvokeFlags.None;
			if (is_virtual)
				flags |= RuntimeInvokeFlags.VirtualMethod;
			if (debug)
				flags |= RuntimeInvokeFlags.BreakOnEntry;
			return RuntimeInvoke (function, object_argument, param_objects, flags);
		}

		public RuntimeInvokeResult RuntimeInvoke (TargetFunctionType function,
							  TargetStructObject object_argument,
							  TargetObject[] param_objects,
							  RuntimeInvokeFlags flags)
		{
			lock (this) {
				check_alive ();
				RuntimeInvokeResult result = new RuntimeInvokeResult (this);
				servant.RuntimeInvoke (
					function, object_argument, param_objects,
					flags, result);
				return result;
			}
		}

		public TargetAddress CallMethod (TargetAddress method, long arg1, long arg2)
		{
			CommandResult result;

			lock (this) {
				check_alive ();
				result = servant.CallMethod (method, arg1, arg2);
			}

			result.Wait ();

			if (result.Result == null)
				throw new TargetException (TargetError.UnknownError);

			return (TargetAddress) result.Result;
		}

		public TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
						 long arg2)
		{
			CommandResult result;

			lock (this) {
				check_alive ();
				result = servant.CallMethod (method, arg1.Address, arg2);
			}

			result.Wait ();

			if (result.Result == null)
				throw new TargetException (TargetError.UnknownError);

			return (TargetAddress) result.Result;
		}

		public TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
						 long arg2, long arg3, string string_arg)
		{
			CommandResult result;

			lock (this) {
				check_alive ();
				result = servant.CallMethod (
					method, arg1.Address, arg2, arg3, string_arg);
			}

			result.Wait ();

			if (result.Result == null)
				throw new TargetException (TargetError.UnknownError);

			return (TargetAddress) result.Result;
		}

		public TargetAddress CallMethod (TargetAddress method, TargetAddress method_argument,
						 TargetObject object_argument)
		{
			CommandResult result;

			lock (this) {
				check_alive ();
				result = servant.CallMethod (
					method, method_argument, object_argument);
			}

			result.Wait ();

			if (result.Result == null)
				throw new TargetException (TargetError.UnknownError);

			return (TargetAddress) result.Result;
		}

		public void Return (ReturnMode mode)
		{
			CommandResult result;

			lock (this) {
				check_alive ();
				result = servant.Return (mode);
				if (result == null)
					return;
			}

			result.Wait ();
		}

		internal void AbortInvocation (long rti_id)
		{
			lock (this) {
				check_alive ();
				servant.AbortInvocation (rti_id);
			}
		}

		public string PrintRegisters (StackFrame frame)
		{
			return Architecture.PrintRegisters (frame);
		}

		public bool HasTarget {
			get { return servant != null; }
		}

		public bool CanRun {
			get { return true; }
		}

		public bool CanStep {
			get { return true; }
		}

		public bool IsStopped {
			get { return (servant != null) && servant.IsStopped; }
		}

		public TargetMemoryInfo TargetMemoryInfo {
			get {
				check_servant ();
				return servant.TargetMemoryInfo;
			}
		}

#region ITargetInfo implementation
		public int TargetAddressSize {
			get { return TargetMemoryInfo.TargetAddressSize; }
		}

		public int TargetIntegerSize {
			get { return TargetMemoryInfo.TargetIntegerSize; }
		}

		public int TargetLongIntegerSize {
			get { return TargetMemoryInfo.TargetLongIntegerSize; }
		}

		public bool IsBigEndian {
			get { return TargetMemoryInfo.IsBigEndian; }
		}
#endregion

#region TargetMemoryAccess implementation
		void write_memory (TargetAddress address, byte[] buffer)
		{
			check_alive ();
			servant.WriteBuffer (address, buffer);
		}

		public AddressDomain AddressDomain {
			get {
				return TargetMemoryInfo.AddressDomain;
			}
		}

		public byte ReadByte (TargetAddress address)
		{
			check_alive ();
			return servant.ReadByte (address);
		}

		public int ReadInteger (TargetAddress address)
		{
			check_alive ();
			return servant.ReadInteger (address);
		}

		public long ReadLongInteger (TargetAddress address)
		{
			check_alive ();
			return servant.ReadLongInteger (address);
		}

		public TargetAddress ReadAddress (TargetAddress address)
		{
			check_alive ();
			return servant.ReadAddress (address);
		}

		public string ReadString (TargetAddress address)
		{
			check_alive ();
			return servant.ReadString (address);
		}

		public TargetBlob ReadMemory (TargetAddress address, int size)
		{
			check_alive ();
			byte[] buffer = servant.ReadBuffer (address, size);
			return new TargetBlob (buffer, TargetMemoryInfo);
		}

		public byte[] ReadBuffer (TargetAddress address, int size)
		{
			check_alive ();
			return servant.ReadBuffer (address, size);
		}

		public bool CanWrite {
			get {
				check_servant ();
				return servant.CanWrite;
			}
		}

		public void WriteBuffer (TargetAddress address, byte[] buffer)
		{
			write_memory (address, buffer);
		}

		public void WriteByte (TargetAddress address, byte value)
		{
			throw new InvalidOperationException ();
		}

		public void WriteInteger (TargetAddress address, int value)
		{
			throw new InvalidOperationException ();
		}

		public void WriteLongInteger (TargetAddress address, long value)
		{
			throw new InvalidOperationException ();
		}

		public void WriteAddress (TargetAddress address, TargetAddress value)
		{
			check_alive ();
			TargetBinaryWriter writer = new TargetBinaryWriter (
				TargetMemoryInfo.TargetAddressSize, TargetMemoryInfo);
			writer.WriteAddress (value);
			write_memory (address, writer.Contents);
		}
#endregion

#region IDisposable implementation
		private bool disposed = false;

		protected void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Thread");
		}

		protected virtual void DoDispose ()
		{
			if (servant != null) {
				servant.Dispose ();
				servant = null;
			}
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

		~Thread ()
		{
			Dispose (false);
		}
	}

	public class ThreadCommandResult : OperationCommandResult
	{
		protected ST.ManualResetEvent completed_event = new ST.ManualResetEvent (false);

		public Thread Thread {
			get; private set;
		}

		internal override IOperationHost Host {
			get { return Thread; }
		}

		internal ThreadCommandResult (Thread thread)
			: base (ThreadingModel.Single)
		{
			this.Thread = thread;
		}

		public override ST.WaitHandle CompletedEvent {
			get { return completed_event; }
		}

		internal override void Completed ()
		{
			completed_event.Set ();
		}

		internal override void OnExecd (SingleSteppingEngine new_thread)
		{
			Thread = new_thread.Thread;
		}
	}

	public class RuntimeInvokeResult : ThreadCommandResult
	{
		internal RuntimeInvokeResult (Thread thread)
			: base (thread)
		{ }

		public override void Abort ()
		{
			Thread.AbortInvocation (ID);
			completed_event.WaitOne ();
		}

		internal override void Completed (SingleSteppingEngine sse, TargetEventArgs args)
		{
			Host.OperationCompleted (sse, args, ThreadingModel);
			if (args != null)
				Host.SendResult (sse, args);
		}

		public long ID;
		public bool InvocationAborted;
		public bool InvocationCompleted;
		public TargetObject ReturnObject;
		public string ExceptionMessage;
		public TargetException TargetException;
	}

	[Flags]
	public enum RuntimeInvokeFlags
	{
		None			= 0,
		NoSideEffects		= 1,
		NestedBreakStates	= 2,
		BreakOnEntry		= 4,
		VirtualMethod		= 8,
		SendEventOnCompletion	= 16
	}

	public enum ReturnMode
	{
		Managed,
		Native,
		Invocation
	}
}
