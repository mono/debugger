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

namespace Mono.Debugger
{
	[Serializable]
	internal delegate object TargetAccessDelegate (Thread target, object user_data);

	public class Thread : TargetAccess
	{
		internal Thread (ThreadServant servant, int id)
		{
			this.id = id;
			this.servant = servant;
			this.operation_completed_event = new ST.ManualResetEvent (false);
		}

		int id;
		ST.ManualResetEvent operation_completed_event;
		ThreadServant servant;

		public ST.WaitHandle WaitHandle {
			get { return operation_completed_event; }
		}

		protected internal Language NativeLanguage {
			get {
				check_servant ();
				return servant.NativeLanguage;
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
		public override TargetState State {
			get {
				if (servant == null)
					return TargetState.NoTarget;
				else
					return servant.State;
			}
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

		internal override Architecture Architecture {
			get {
				check_servant ();
				return servant.Architecture;
			}
		}

		internal override ProcessServant ProcessServant {
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

		internal override ThreadManager ThreadManager {
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

		public bool IsDaemon {
			get {
				check_servant ();
				return servant.IsDaemon;
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

		// <summary>
		//   The current stack frame.  May only be used when the engine is stopped
		//   (State == TargetState.STOPPED).  The single stepping engine
		//   automatically computes the current frame and current method each time
		//   a stepping operation is completed.  This ensures that we do not
		//   unnecessarily compute this several times if more than one client
		//   accesses this property.
		// </summary>
		public override StackFrame CurrentFrame {
			get {
				check_servant ();
				return servant.CurrentFrame;
			}
		}

		public override TargetAddress CurrentFrameAddress {
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
		public override Backtrace GetBacktrace (int max_frames)
		{
			check_servant ();
			return servant.GetBacktrace (max_frames);
		}

		public Backtrace GetBacktrace ()
		{
			check_servant ();
			Backtrace bt = servant.CurrentBacktrace;
			if (bt != null)
				return bt;

			return GetBacktrace (-1);
		}

		public override Backtrace CurrentBacktrace {
			get {
				check_servant ();
				return servant.CurrentBacktrace;
			}
		}

		public override Registers GetRegisters ()
		{
			check_servant ();
			return servant.GetRegisters ();
		}

		public override void SetRegisters (Registers registers)
		{
			check_servant ();
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

		// <summary>
		//   Step one machine instruction, but don't step into trampolines.
		// </summary>
		public CommandResult StepInstruction ()
		{
			lock (this) {
				check_servant ();
				operation_completed_event.Reset ();
				CommandResult result = new StepCommandResult (this);
				servant.StepInstruction (result);
				return result;
			}
		}

		// <summary>
		//   Step one machine instruction, always step into method calls.
		// </summary>
		public CommandResult StepNativeInstruction ()
		{
			lock (this) {
				check_servant ();
				operation_completed_event.Reset ();
				CommandResult result = new StepCommandResult (this);
				servant.StepNativeInstruction (result);
				return result;
			}
		}

		// <summary>
		//   Step one machine instruction, but step over method calls.
		// </summary>
		public CommandResult NextInstruction ()
		{
			lock (this) {
				check_servant ();
				operation_completed_event.Reset ();
				CommandResult result = new StepCommandResult (this);
				servant.NextInstruction (result);
				return result;
			}
		}

		// <summary>
		//   Step one source line.
		// </summary>
		public CommandResult StepLine ()
		{
			lock (this) {
				check_servant ();
				operation_completed_event.Reset ();
				CommandResult result = new StepCommandResult (this);
				servant.StepLine (result);
				return result;
			}
		}

		// <summary>
		//   Step one source line, but step over method calls.
		// </summary>
		public CommandResult NextLine ()
		{
			lock (this) {
				check_servant ();
				operation_completed_event.Reset ();
				CommandResult result = new StepCommandResult (this);
				servant.NextLine (result);
				return result;
			}
		}

		// <summary>
		//   Continue until leaving the current method.
		// </summary>
		public CommandResult Finish (bool native)
		{
			lock (this) {
				check_servant ();
				operation_completed_event.Reset ();
				CommandResult result = new StepCommandResult (this);
				servant.Finish (native, result);
				return result;
			}
		}

		public CommandResult Continue ()
		{
			return Continue (TargetAddress.Null, false);
		}

		public CommandResult Continue (TargetAddress until)
		{
			return Continue (until, false);
		}

		public CommandResult Continue (bool in_background)
		{
			return Continue (TargetAddress.Null, in_background);
		}

		public CommandResult Continue (TargetAddress until, bool in_background)
		{
			lock (this) {
				check_servant ();
				operation_completed_event.Reset ();
				CommandResult result = new StepCommandResult (this);
				servant.Continue (until, in_background, new StepCommandResult (this));
				return result;
			}
		}

		internal void Kill ()
		{
			operation_completed_event.Set ();
			if (servant != null)
				servant.Kill ();
			Dispose ();
		}

		internal void Detach ()
		{
			operation_completed_event.Set ();
			if (servant != null)
				servant.Detach ();
			Dispose ();
		}

		public void Stop ()
		{
			check_servant ();
			servant.Stop ();
		}

		public void Wait ()
		{
			Report.Debug (DebugFlags.Wait, "{0} waiting", this);
			operation_completed_event.WaitOne ();
			Report.Debug (DebugFlags.Wait, "{0} done waiting", this);
		}

		// <summary>
		//   Insert a breakpoint at address @address.
		//
		//   Returns a number which may be passed to RemoveBreakpoint() to remove
		//   the breakpoint.
		// </summary>
		public override int InsertBreakpoint (Breakpoint breakpoint, TargetAddress address)
		{
			check_servant ();
			return servant.InsertBreakpoint (breakpoint, address);
		}

		// <summary>
		//   Remove breakpoint @index.  @index is the breakpoint number which has
		//   been returned by InsertBreakpoint().
		// </summary>
		public void RemoveBreakpoint (int index)
		{
			check_disposed ();
			if (servant != null)
				servant.RemoveBreakpoint (index);
		}

		// <summary>
		//   Insert a breakpoint at function @func.
		//
		//   Returns a number which may be passed to RemoveBreakpoint() to remove
		//   the breakpoint.
		// </summary>
		public int InsertBreakpoint (Breakpoint breakpoint, TargetFunctionType func)
		{
			CommandResult result;

			lock (this) {
				check_servant ();
				result = servant.InsertBreakpoint (breakpoint, func);
			}

			result.Wait ();

			return (int) result.Result;
		}

		// <summary>
		//   Add an event handler.
		//
		//   Returns a number which may be passed to RemoveEventHandler() to remove
		//   the event handler.
		// </summary>
		public int AddEventHandler (EventType type, Event handle)
		{
			check_servant ();
			return servant.AddEventHandler (type, handle);
		}

		// <summary>
		//   Remove event handler @index.  @index is the event handler number which has
		//   been returned by AddEventHandler().
		// </summary>
		public void RemoveEventHandler (int index)
		{
			check_disposed ();
			if (servant != null)
				servant.RemoveEventHandler (index);
		}

		public string PrintObject (Style style, TargetObject obj, DisplayFormat format)
		{
			check_servant ();
			return servant.PrintObject (style, obj, format);
		}

		public string PrintType (Style style, TargetType type)
		{
			check_servant ();
			return servant.PrintType (style, type);
		}

		//
		// Disassembling.
		//

		public override int GetInstructionSize (TargetAddress address)
		{
			check_servant ();
			return servant.GetInstructionSize (address);
		}

		public override AssemblerLine DisassembleInstruction (Method method, TargetAddress address)
		{
			check_servant ();
			return servant.DisassembleInstruction (method, address);
		}

		public override AssemblerMethod DisassembleMethod (Method method)
		{
			check_servant ();
			return servant.DisassembleMethod (method);
		}

		public void RuntimeInvoke (TargetFunctionType function,
					   TargetClassObject object_argument,
					   TargetObject[] param_objects,
					   bool is_virtual)
		{
			CommandResult result;

			lock (this) {
				check_servant ();
				result = servant.RuntimeInvoke (
					function, object_argument, param_objects, is_virtual, true);
			}

			result.Wait ();
		}

		public TargetObject RuntimeInvoke (TargetFunctionType function,
						   TargetClassObject object_argument,
						   TargetObject[] param_objects,
						   bool is_virtual, out string exc_message)
		{
			CommandResult result;

			lock (this) {
				check_servant ();
				result = servant.RuntimeInvoke (
					function, object_argument, param_objects, is_virtual, false);
			}

			result.Wait ();

			RuntimeInvokeResult res = (RuntimeInvokeResult) result.Result;
			if (res == null) {
				exc_message = null;
				return null;
			}
			exc_message = res.ExceptionMessage;
			return res.ReturnObject;
		}

		public TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
						 TargetAddress arg2)
		{
			CommandResult result;

			lock (this) {
				check_servant ();
				result = servant.CallMethod (method, arg1, arg2);
			}

			result.Wait ();

			if (result.Result == null)
				throw new TargetException (TargetError.UnknownError);

			return (TargetAddress) result.Result;
		}

		public TargetAddress CallMethod (TargetAddress method, long method_arg,
						 string string_arg)
		{
			CommandResult result;

			lock (this) {
				check_servant ();
				result = servant.CallMethod (method, method_arg, string_arg);
			}

			result.Wait ();

			if (result.Result == null)
				throw new TargetException (TargetError.UnknownError);

			return (TargetAddress) result.Result;
		}

		public void Return (bool run_finally)
		{
			CommandResult result;

			lock (this) {
				check_servant ();
				result = servant.Return (run_finally);
				if (result == null)
					return;
			}

			result.Wait ();
		}

		public void AbortInvocation ()
		{
			CommandResult result;

			lock (this) {
				check_servant ();
				result = servant.AbortInvocation ();
			}

			result.Wait ();
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
			get { return State == TargetState.Stopped; }
		}

		public override TargetInfo TargetInfo {
			get {
				check_servant ();
				return servant.TargetInfo;
			}
		}

#region ITargetInfo implementation
		public override int TargetAddressSize {
			get { return TargetInfo.TargetAddressSize; }
		}

		public override int TargetIntegerSize {
			get { return TargetInfo.TargetIntegerSize; }
		}

		public override int TargetLongIntegerSize {
			get { return TargetInfo.TargetLongIntegerSize; }
		}

		public override bool IsBigEndian {
			get { return TargetInfo.IsBigEndian; }
		}
#endregion

#region TargetMemoryAccess implementation
		void write_memory (TargetAddress address, byte[] buffer)
		{
			check_servant ();
			servant.WriteBuffer (address, buffer);
		}

		public override AddressDomain AddressDomain {
			get {
				return TargetInfo.AddressDomain;
			}
		}

		public override byte ReadByte (TargetAddress address)
		{
			check_servant ();
			return servant.ReadByte (address);
		}

		public override int ReadInteger (TargetAddress address)
		{
			check_servant ();
			return servant.ReadInteger (address);
		}

		public override long ReadLongInteger (TargetAddress address)
		{
			check_servant ();
			return servant.ReadLongInteger (address);
		}

		public override TargetAddress ReadAddress (TargetAddress address)
		{
			check_servant ();
			return servant.ReadAddress (address);
		}

		public override string ReadString (TargetAddress address)
		{
			check_servant ();
			return servant.ReadString (address);
		}

		public override TargetBlob ReadMemory (TargetAddress address, int size)
		{
			check_servant ();
			byte[] buffer = servant.ReadBuffer (address, size);
			return new TargetBlob (buffer, TargetInfo);
		}

		public override byte[] ReadBuffer (TargetAddress address, int size)
		{
			check_servant ();
			return servant.ReadBuffer (address, size);
		}

		public override bool CanWrite {
			get {
				check_servant ();
				return servant.CanWrite;
			}
		}

		public override void WriteBuffer (TargetAddress address, byte[] buffer)
		{
			write_memory (address, buffer);
		}

		public override void WriteByte (TargetAddress address, byte value)
		{
			throw new InvalidOperationException ();
		}

		public override void WriteInteger (TargetAddress address, int value)
		{
			throw new InvalidOperationException ();
		}

		public override void WriteLongInteger (TargetAddress address, long value)
		{
			throw new InvalidOperationException ();
		}

		public override void WriteAddress (TargetAddress address, TargetAddress value)
		{
			check_servant ();
			TargetBinaryWriter writer = new TargetBinaryWriter (
				TargetInfo.TargetAddressSize, TargetInfo);
			writer.WriteAddress (value);
			write_memory (address, writer.Contents);
		}
#endregion

		internal class StepCommandResult : CommandResult
		{
			Thread thread;

			public StepCommandResult (Thread thread)
			{
				this.thread = thread;
			}

			public override ST.WaitHandle CompletedEvent {
				get { return thread.WaitHandle; }
			}

			public override void Completed ()
			{
				thread.operation_completed_event.Set ();
			}
		}

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

				operation_completed_event.Set ();
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

	public abstract class CommandResult : DebuggerMarshalByRefObject
	{
		public object Result;

		public abstract ST.WaitHandle CompletedEvent {
			get;
		}

		public abstract void Completed ();

		public void Wait ()
		{
			CompletedEvent.WaitOne ();
			if (Result is Exception)
				throw (Exception) Result;
		}
	}
}
