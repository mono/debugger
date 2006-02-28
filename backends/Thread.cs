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
using Mono.Debugger.Remoting;

namespace Mono.Debugger
{
	using SSE = SingleSteppingEngine;

	public class Thread : ThreadBase
	{
		internal Thread (SingleSteppingEngine engine)
		{
			this.engine = engine;
			this.thread = engine;
			this.id = engine.ThreadManager.NextThreadID;

			this.manager = engine.ThreadManager;
			this.process = engine.Process;

			this.symtab_manager = process.SymbolTableManager;

			this.pid = engine.PID;
			this.tid = engine.TID;

			tgroup = process.CreateThreadGroup ("@" + ID);
			tgroup.AddThread (ID);

			operation_completed_event = new ST.ManualResetEvent (false);

			this.target_info = engine.TargetInfo;
			this.target_memory_info = engine.TargetMemoryInfo;
		}

		internal Thread (ThreadBase thread, int pid)
		{
			this.thread = thread;
			this.id = thread.ThreadManager.NextThreadID;
			this.pid = pid;

			this.manager = thread.ThreadManager;
			this.process = thread.Process;

			this.symtab_manager = process.SymbolTableManager;

			tgroup = process.CreateThreadGroup ("@" + ID);
			tgroup.AddThread (ID);

			operation_completed_event = new ST.ManualResetEvent (false);

			this.target_info = thread.TargetInfo;
			this.target_memory_info = thread.TargetMemoryInfo;
		}

		bool is_daemon;
		int id, pid;
		long tid;
		ThreadGroup tgroup;
		ThreadBase thread;
		SingleSteppingEngine engine;
		Process process;
		ThreadManager manager;
		SymbolTableManager symtab_manager;
		ST.ManualResetEvent operation_completed_event;
		ITargetInfo target_info;
		ITargetMemoryInfo target_memory_info;

		public ST.WaitHandle WaitHandle {
			get { return operation_completed_event; }
		}

		protected internal Language NativeLanguage {
			get {
				check_thread ();
				return process.BfdContainer.NativeLanguage;
			}
		}

		public event TargetOutputHandler TargetOutput;

		internal void OnInferiorOutput (bool is_stderr, string line)
		{
			if (TargetOutput != null)
				TargetOutput (is_stderr, line);
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
				if (thread == null)
					return TargetState.NO_TARGET;
				else
					return thread.State;
			}
		}

		public int ID {
			get {
				return id;
			}
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
				return pid;
			}
		}

		public long TID {
			get {
				return tid;
			}
		}

		public override Architecture Architecture {
			get {
				check_thread ();
				return target_memory_info.Architecture;
			}
		}

		public override Process Process {
			get {
				check_thread ();
				return process;
			}
		}

		internal override ThreadManager ThreadManager {
			get {
				check_thread ();
				return manager;
			}
		}

		public bool IsDaemon {
			get {
				return is_daemon;
			}
		}

		public ThreadGroup ThreadGroup {
			get { return tgroup; }
		}

		internal void SetDaemon ()
		{
			is_daemon = true;
		}

		internal void SetTID (long tid)
		{
			this.tid = tid;
		}

		void check_engine ()
		{
			if (engine == null)
				throw new TargetException (TargetError.NoTarget);
		}

		void check_thread ()
		{
			if (thread == null)
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
				check_thread ();
				return thread.CurrentFrame;
			}
		}

		public override TargetAddress CurrentFrameAddress {
			get {
				check_thread ();
				return thread.CurrentFrameAddress;
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
			check_thread ();
			return thread.GetBacktrace (max_frames);
		}

		public Backtrace GetBacktrace ()
		{
			check_thread ();
			Backtrace bt = thread.CurrentBacktrace;
			if (bt != null)
				return bt;

			return GetBacktrace (-1);
		}

		public override Backtrace CurrentBacktrace {
			get {
				check_thread ();
				return thread.CurrentBacktrace;
			}
		}

		public override Registers GetRegisters ()
		{
			check_thread ();
			return thread.GetRegisters ();
		}

		public override void SetRegisters (Registers registers)
		{
			check_engine ();
			engine.SetRegisters (registers);
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			check_engine ();
			return engine.GetMemoryMaps ();
		}

		public Method Lookup (TargetAddress address)
		{
			check_engine ();
			return symtab_manager.Lookup (address);
		}

		public Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			check_engine ();
			return symtab_manager.SimpleLookup (address, exact_match);
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
				check_engine ();
				return engine.CurrentMethod;
			}
		}

		// <summary>
		//   Step one machine instruction, but don't step into trampolines.
		// </summary>
		public void StepInstruction ()
		{
			lock (this) {
				check_engine ();
				operation_completed_event.Reset ();
				engine.StepInstruction (new StepCommandResult (this));
			}
		}

		// <summary>
		//   Step one machine instruction, always step into method calls.
		// </summary>
		public void StepNativeInstruction ()
		{
			lock (this) {
				check_engine ();
				operation_completed_event.Reset ();
				engine.StepNativeInstruction (new StepCommandResult (this));
			}
		}

		// <summary>
		//   Step one machine instruction, but step over method calls.
		// </summary>
		public void NextInstruction ()
		{
			lock (this) {
				check_engine ();
				operation_completed_event.Reset ();
				engine.NextInstruction (new StepCommandResult (this));
			}
		}

		// <summary>
		//   Step one source line.
		// </summary>
		public void StepLine ()
		{
			lock (this) {
				check_engine ();
				operation_completed_event.Reset ();
				engine.StepLine (new StepCommandResult (this));
			}
		}

		// <summary>
		//   Step one source line, but step over method calls.
		// </summary>
		public void NextLine ()
		{
			lock (this) {
				check_engine ();
				operation_completed_event.Reset ();
				engine.NextLine (new StepCommandResult (this));
			}
		}

		// <summary>
		//   Continue until leaving the current method.
		// </summary>
		public void Finish ()
		{
			lock (this) {
				check_engine ();
				operation_completed_event.Reset ();
				engine.Finish (new StepCommandResult (this));
			}
		}

		// <summary>
		//   Continue until leaving the current method.
		// </summary>
		public void FinishNative ()
		{
			lock (this) {
				check_engine ();
				operation_completed_event.Reset ();
				engine.FinishNative (new StepCommandResult (this));
			}
		}

		public void Continue ()
		{
			Continue (TargetAddress.Null, false);
		}

		public void Continue (TargetAddress until)
		{
			Continue (until, false);
		}

		public void Continue (bool in_background)
		{
			Continue (TargetAddress.Null, in_background);
		}

		public void Continue (TargetAddress until, bool in_background)
		{
			lock (this) {
				check_engine ();
				operation_completed_event.Reset ();
				engine.Continue (until, in_background, new StepCommandResult (this));
			}
		}

		internal void Kill ()
		{
			operation_completed_event.Set ();
			Dispose ();
		}

		public void Stop ()
		{
			check_engine ();
			engine.Stop ();
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
			check_engine ();
			return engine.InsertBreakpoint (breakpoint, address);
		}

		// <summary>
		//   Remove breakpoint @index.  @index is the breakpoint number which has
		//   been returned by InsertBreakpoint().
		// </summary>
		public void RemoveBreakpoint (int index)
		{
			check_disposed ();
			if (engine != null)
				engine.RemoveBreakpoint (index);
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
				check_engine ();
				result = engine.InsertBreakpoint (breakpoint, func);
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
		public void AddEventHandler (EventType type, EventHandle handle)
		{
			check_engine ();
			engine.AddEventHandler (type, handle);
		}

		// <summary>
		//   Remove event handler @index.  @index is the event handler number which has
		//   been returned by AddEventHandler().
		// </summary>
		public void RemoveEventHandler (int index)
		{
			check_disposed ();
			if (engine != null)
				engine.RemoveEventHandler (index);
		}

		public string PrintObject (Style style, TargetObject obj, DisplayFormat format)
		{
			check_engine ();
			return engine.PrintObject (style, obj, format);
		}

		public string PrintType (Style style, TargetType type)
		{
			check_engine ();
			return engine.PrintType (style, type);
		}

		//
		// Disassembling.
		//

		public override int GetInstructionSize (TargetAddress address)
		{
			check_thread ();
			return thread.GetInstructionSize (address);
		}

		public override AssemblerLine DisassembleInstruction (Method method, TargetAddress address)
		{
			check_thread ();
			return thread.DisassembleInstruction (method, address);
		}

		public override AssemblerMethod DisassembleMethod (Method method)
		{
			check_thread ();
			return thread.DisassembleMethod (method);
		}

		public void RuntimeInvoke (TargetFunctionType function,
					   TargetClassObject object_argument,
					   TargetObject[] param_objects,
					   bool is_virtual)
		{
			CommandResult result;

			lock (this) {
				check_engine ();
				result = engine.RuntimeInvoke (
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
				check_engine ();
				result = engine.RuntimeInvoke (
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

		public TargetAddress CallMethod (TargetAddress method, long method_arg,
						 string string_arg)
		{
			CommandResult result;

			lock (this) {
				check_engine ();
				result = engine.CallMethod (method, method_arg, string_arg);
			}

			result.Wait ();

			if (result.Result == null)
				throw new TargetException (TargetError.UnknownError);

			return (TargetAddress) result.Result;
		}

		public object Invoke (TargetAccessDelegate func, object data)
		{
			if (engine.ThreadManager.InBackgroundThread)
				return func (this, data);
			else
				return engine.ThreadManager.SendCommand (engine, func, data);
		}

		public void Return (bool run_finally)
		{
			CommandResult result;

			lock (this) {
				check_engine ();
				result = engine.Return (run_finally);
				if (result == null)
					return;
			}

			result.Wait ();
		}

		public void AbortInvocation ()
		{
			CommandResult result;

			lock (this) {
				check_engine ();
				result = engine.AbortInvocation ();
			}

			result.Wait ();
		}

		public bool HasTarget {
			get { return engine != null; }
		}

		public bool CanRun {
			get { return true; }
		}

		public bool CanStep {
			get { return true; }
		}

		public bool IsStopped {
			get { return State == TargetState.STOPPED; }
		}

		public TargetAccess TargetAccess {
			get { return engine.TargetAccess; }
		}

		public ITargetMemoryAccess TargetMemoryAccess {
			get { return this; }
		}

		public override ITargetInfo TargetInfo {
			get { return target_info; }
		}

		public override ITargetMemoryInfo TargetMemoryInfo {
			get { return target_memory_info; }
		}

#region ITargetInfo implementation
		public override int TargetAddressSize {
			get { return target_info.TargetAddressSize; }
		}

		public override int TargetIntegerSize {
			get { return target_info.TargetIntegerSize; }
		}

		public override int TargetLongIntegerSize {
			get { return target_info.TargetLongIntegerSize; }
		}

		public override bool IsBigEndian {
			get { return target_info.IsBigEndian; }
		}
#endregion

#region ITargetMemoryAccess implementation
		void write_memory (TargetAddress address, byte[] buffer)
		{
			check_thread ();
			thread.WriteBuffer (address, buffer);
		}

		public override AddressDomain AddressDomain {
			get {
				return target_memory_info.AddressDomain;
			}
		}

		public override byte ReadByte (TargetAddress address)
		{
			check_thread ();
			return thread.ReadByte (address);
		}

		public override int ReadInteger (TargetAddress address)
		{
			check_thread ();
			return thread.ReadInteger (address);
		}

		public override long ReadLongInteger (TargetAddress address)
		{
			check_thread ();
			return thread.ReadLongInteger (address);
		}

		public override TargetAddress ReadAddress (TargetAddress address)
		{
			check_thread ();
			return thread.ReadAddress (address);
		}

		public override string ReadString (TargetAddress address)
		{
			check_thread ();
			return thread.ReadString (address);
		}

		public override TargetBlob ReadMemory (TargetAddress address, int size)
		{
			check_thread ();
			byte[] buffer = thread.ReadBuffer (address, size);
			return new TargetBlob (buffer, target_info);
		}

		public override byte[] ReadBuffer (TargetAddress address, int size)
		{
			check_thread ();
			return thread.ReadBuffer (address, size);
		}

		public override bool CanWrite {
			get { return false; }
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
			check_thread ();
			TargetBinaryWriter writer = new TargetBinaryWriter (
				target_info.TargetAddressSize, target_info);
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
			if (engine != null) {
				engine.Dispose ();
				engine = null;

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
}
