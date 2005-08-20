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

namespace Mono.Debugger
{
	using SSE = SingleSteppingEngine;

	public delegate bool BreakpointCheckHandler (StackFrame frame, ITargetAccess target,
						     int index, object user_data);
	public delegate void BreakpointHitHandler (StackFrame frame, int index,
						   object user_data);
	public delegate void ProcessExitedHandler (Process process);

	public class Process : MarshalByRefObject, ITargetAccess, IDisassembler
	{
		internal Process (SingleSteppingEngine engine)
		{
			this.engine = engine;
			this.id = engine.ThreadManager.NextProcessID;
		}

		int id;
		SingleSteppingEngine engine;

		protected internal ILanguage NativeLanguage {
			get {
				check_engine ();
				return DebuggerBackend.BfdContainer.NativeLanguage;
			}
		}

		protected virtual void OnTargetEvent (TargetEventArgs args)
		{
			if (TargetEvent != null)
				TargetEvent (this, args);
		}

		protected virtual void OnTargetExitedEvent ()
		{
			if (TargetExitedEvent != null)
				TargetExitedEvent ();
		}

		protected virtual void OnProcessExitedEvent ()
		{
			if (ProcessExitedEvent != null)
				ProcessExitedEvent (this);
		}

		public event TargetOutputHandler TargetOutput;
		public event DebuggerOutputHandler DebuggerOutput;
		public event DebuggerErrorHandler DebuggerError;

		// <summary>
		//   This event is emitted each time a stepping operation is started or
		//   completed.  Other than the Inferior's StateChangedEvent, it is only
		//   emitted after the whole operation completed.
		// </summary>
		public event TargetEventHandler TargetEvent;
		public event TargetExitedHandler TargetExitedEvent;
		public event ProcessExitedHandler ProcessExitedEvent;

		internal void OnInferiorOutput (bool is_stderr, string line)
		{
			if (TargetOutput != null)
				TargetOutput (is_stderr, line);
		}

		internal void OnDebuggerOutput (string line)
		{
			if (DebuggerOutput != null)
				DebuggerOutput (line);
		}

		internal void OnDebuggerError (object sender, string message,
							Exception e)
		{
			if (DebuggerError != null)
				DebuggerError (this, message, e);
		}

		// <summary>
		//   The single-stepping engine's target state.  This will be
		//   TargetState.RUNNING while the engine is stepping.
		// </summary>
		public TargetState State {
			get {
				if (engine == null)
					return TargetState.NO_TARGET;
				else
					return engine.State;
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
					return String.Format ("Daemon process @{0}", id);
				else
					return String.Format ("Process @{0}", id);
			}
		}

		public int PID {
			get {
				return engine.PID;
			}
		}

		public int TID {
			get {
				return engine.TID;
			}
		}

		public bool IsDaemon {
			get {
				check_engine ();
				return engine.IsDaemon;
			}
		}

		public IArchitecture Architecture {
			get {
				check_engine ();
				return engine.Architecture;
			}
		}

		public DebuggerBackend DebuggerBackend {
			get {
				check_engine ();
				return engine.ThreadManager.DebuggerBackend;
			}
		}

		void check_engine ()
		{
			if (engine == null)
				throw new TargetException (TargetError.NoTarget);
		}

		internal void SendTargetEvent (TargetEventArgs args)
		{
			if ((args.Type == TargetEventType.TargetSignaled) ||
			    (args.Type == TargetEventType.TargetExited))
				OnTargetExitedEvent ();

			OnTargetEvent (args);
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
				check_engine ();
				return engine.CurrentFrame;
			}
		}

		public TargetAddress CurrentFrameAddress {
			get {
				StackFrame frame = CurrentFrame;
				return frame != null ? frame.TargetAddress : TargetAddress.Null;
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
		public Backtrace GetBacktrace (int max_frames)
		{
			check_engine ();
			return engine.GetBacktrace (max_frames);
		}

		public Backtrace GetBacktrace ()
		{
			Backtrace bt = engine.CurrentBacktrace;
			if (bt != null)
				return bt;

			return GetBacktrace (-1);
		}

		public Backtrace UnwindStack (TargetAddress stack_pointer)
		{
			check_engine ();
			Backtrace bt = new Backtrace (
				this, engine.Architecture, CurrentFrame);
			bt.GetBacktrace (
				this, engine.Architecture, engine.SymbolTable,
				SimpleSymbolTable, stack_pointer);
			return bt;
		}

		public Registers GetRegisters ()
		{
			check_engine ();
			return engine.GetRegisters ();
		}

		public void SetRegisters (Registers registers)
		{
			check_engine ();
			engine.SetRegisters (registers);
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			check_engine ();
			return engine.GetMemoryMaps ();
		}

		protected Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			check_engine ();
			return engine.SimpleLookup (address, exact_match);
		}

		// <summary>
		//   The current method  May only be used when the engine is stopped
		//   (State == TargetState.STOPPED).  The single stepping engine
		//   automatically computes the current frame and current method each time
		//   a stepping operation is completed.  This ensures that we do not
		//   unnecessarily compute this several times if more than one client
		//   accesses this property.
		// </summary>
		public IMethod CurrentMethod {
			get {
				check_engine ();
				return engine.CurrentMethod;
			}
		}

		// <summary>
		//   Step one machine instruction, but don't step into trampolines.
		// </summary>
		public bool StepInstruction (bool wait)
		{
			check_engine ();
			return engine.StepInstruction (wait);
		}

		// <summary>
		//   Step one machine instruction, always step into method calls.
		// </summary>
		public bool StepNativeInstruction (bool wait)
		{
			check_engine ();
			return engine.StepNativeInstruction (wait);
		}

		// <summary>
		//   Step one machine instruction, but step over method calls.
		// </summary>
		public bool NextInstruction (bool wait)
		{
			check_engine ();
			return engine.NextInstruction (wait);
		}

		// <summary>
		//   Step one source line.
		// </summary>
		public bool StepLine (bool wait)
		{
			check_engine ();
			return engine.StepLine (wait);
		}

		// <summary>
		//   Step one source line, but step over method calls.
		// </summary>
		public bool NextLine (bool wait)
		{
			check_engine ();
			return engine.NextLine (wait);
		}

		// <summary>
		//   Continue until leaving the current method.
		// </summary>
		public bool Finish (bool wait)
		{
			check_engine ();
			return engine.Finish (wait);
		}

		// <summary>
		//   Continue until leaving the current method.
		// </summary>
		public bool FinishNative (bool wait)
		{
			check_engine ();
			return engine.FinishNative (wait);
		}

		public bool Continue (TargetAddress until, bool synchronous)
		{
			return Continue (until, false, synchronous);
		}

		public bool Continue (bool in_background, bool synchronous)
		{
			return Continue (TargetAddress.Null, in_background, synchronous);
		}

		public bool Continue (bool synchronous)
		{
			return Continue (TargetAddress.Null, false, synchronous);
		}

		public bool Continue (TargetAddress until, bool in_background, bool wait)
		{
			check_engine ();
			return engine.Continue (until, in_background, wait);
		}

		public void Kill ()
		{
			OnProcessExitedEvent ();
			Dispose ();
		}

		public bool Stop ()
		{
			check_engine ();
			return engine.Stop ();
		}

		public bool Wait ()
		{
			check_engine ();
			return engine.Wait ();
		}

		// <summary>
		//   Insert a breakpoint at address @address.
		//
		//   Returns a number which may be passed to RemoveBreakpoint() to remove
		//   the breakpoint.
		// </summary>
		public int InsertBreakpoint (Breakpoint breakpoint, TargetAddress address)
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
		//   Add an event handler.
		//
		//   Returns a number which may be passed to RemoveEventHandler() to remove
		//   the event handler.
		// </summary>
		public int AddEventHandler (EventType type, Breakpoint breakpoint)
		{
			check_engine ();
			return engine.AddEventHandler (type, breakpoint);
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

		public ISimpleSymbolTable SimpleSymbolTable {
			get {
				check_engine ();
				return engine.SimpleSymbolTable;
			}
		}

		//
		// Disassembling.
		//

		public IDisassembler Disassembler {
			get { return this; }
		}

		ISimpleSymbolTable IDisassembler.SymbolTable {
			get {
				check_engine ();
				return engine.SimpleSymbolTable;
			}

			set {
				check_engine ();
				engine.SimpleSymbolTable = value;
			}
		}

		public int GetInstructionSize (TargetAddress address)
		{
			check_engine ();
			return engine.GetInstructionSize (address);
		}

		public AssemblerLine DisassembleInstruction (IMethod method, TargetAddress address)
		{
			check_engine ();
			return engine.DisassembleInstruction (method, address);
		}

		public AssemblerMethod DisassembleMethod (IMethod method)
		{
			check_engine ();
			return engine.DisassembleMethod (method);
		}

		public long CallMethod (TargetAddress method, long method_argument,
					string string_argument)
		{
			check_engine ();
			return engine.CallMethod (method, method_argument, string_argument);
		}

		public TargetAddress CallMethod (TargetAddress method, string arg)
		{
			check_engine ();
			return engine.CallMethod (method, arg);
		}

		public TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
						 TargetAddress arg2)
		{
			check_engine ();
			return engine.CallMethod (method, arg1, arg2);
		}

		public bool RuntimeInvoke (StackFrame frame,
					   TargetAddress method_argument,
					   TargetAddress object_argument,
					   TargetAddress[] param_objects)
		{
			check_engine ();
			return engine.RuntimeInvoke (
				frame, method_argument, object_argument, param_objects);
		}

		public TargetAddress RuntimeInvoke (StackFrame frame,
						    TargetAddress method_argument,
						    TargetAddress object_argument,
						    TargetAddress[] param_objects,
						    out TargetAddress exc_object)
		{
			check_engine ();
			return engine.RuntimeInvoke (
				frame, method_argument, object_argument, param_objects,
				out exc_object);
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

		public ITargetAccess TargetAccess {
			get { return this; }
		}

		public ITargetMemoryAccess TargetMemoryAccess {
			get { return this; }
		}

		public ITargetMemoryInfo TargetMemoryInfo {
			get { return this; }
		}

#region ITargetInfo implementation
		int ITargetInfo.TargetAddressSize {
			get {
				check_engine ();
				return engine.TargetAddressSize;
			}
		}

		int ITargetInfo.TargetIntegerSize {
			get {
				check_engine ();
				return engine.TargetIntegerSize;
			}
		}

		int ITargetInfo.TargetLongIntegerSize {
			get {
				check_engine ();
				return engine.TargetLongIntegerSize;
			}
		}

		bool ITargetInfo.IsBigEndian {
			get {
				check_engine ();
				return engine.IsBigEndian;
			}
		}
#endregion

#region ITargetMemoryAccess implementation
		byte[] read_memory (TargetAddress address, int size)
		{
			check_engine ();
			return engine.ReadMemory (address, size);
		}

		string read_string (TargetAddress address)
		{
			check_engine ();
			return engine.ReadString (address);
		}

		ITargetMemoryReader get_memory_reader (TargetAddress address, int size)
		{
			byte[] buffer = read_memory (address, size);
			return new TargetReader (buffer, this);
		}

		void write_memory (TargetAddress address, byte[] buffer)
		{
			check_engine ();
			engine.WriteMemory (address, buffer);
		}

		AddressDomain ITargetMemoryInfo.AddressDomain {
			get {
				return engine.AddressDomain;
			}
		}

		AddressDomain ITargetMemoryInfo.GlobalAddressDomain {
			get {
				return engine.GlobalAddressDomain;
			}
		}

		byte ITargetMemoryAccess.ReadByte (TargetAddress address)
		{
			byte[] data = read_memory (address, 1);
			return data [0];
		}

		int ITargetMemoryAccess.ReadInteger (TargetAddress address)
		{
			check_engine ();
			ITargetMemoryReader reader = get_memory_reader (address, engine.TargetIntegerSize);
			return reader.ReadInteger ();
		}

		long ITargetMemoryAccess.ReadLongInteger (TargetAddress address)
		{
			check_engine ();
			ITargetMemoryReader reader = get_memory_reader (address, engine.TargetLongIntegerSize);
			return reader.ReadLongInteger ();
		}

		TargetAddress ITargetMemoryAccess.ReadAddress (TargetAddress address)
		{
			check_engine ();
			ITargetMemoryReader reader = get_memory_reader (address, engine.TargetAddressSize);
			return reader.ReadAddress ();
		}

		TargetAddress ITargetMemoryAccess.ReadGlobalAddress (TargetAddress address)
		{
			check_engine ();
			ITargetMemoryReader reader = get_memory_reader (address, engine.TargetAddressSize);
			return reader.ReadGlobalAddress ();
		}

		string ITargetMemoryAccess.ReadString (TargetAddress address)
		{
			return read_string (address);
		}

		ITargetMemoryReader ITargetMemoryAccess.ReadMemory (TargetAddress address, int size)
		{
			return get_memory_reader (address, size);
		}

		ITargetMemoryReader ITargetMemoryAccess.ReadMemory (byte[] buffer)
		{
			return new TargetReader (buffer, this);
		}

		byte[] ITargetMemoryAccess.ReadBuffer (TargetAddress address, int size)
		{
			return read_memory (address, size);
		}

		bool ITargetMemoryAccess.CanWrite {
			get { return false; }
		}
#endregion

#region ITargetAccess implementation
		void ITargetAccess.WriteBuffer (TargetAddress address, byte[] buffer)
		{
			write_memory (address, buffer);
		}

		void ITargetAccess.WriteByte (TargetAddress address, byte value)
		{
			throw new InvalidOperationException ();
		}

		void ITargetAccess.WriteInteger (TargetAddress address, int value)
		{
			throw new InvalidOperationException ();
		}

		void ITargetAccess.WriteLongInteger (TargetAddress address, long value)
		{
			throw new InvalidOperationException ();
		}

		void ITargetAccess.WriteAddress (TargetAddress address, TargetAddress value)
		{
			check_engine ();
			TargetBinaryWriter writer = new TargetBinaryWriter (engine.TargetAddressSize, this);
			writer.WriteAddress (value);
			write_memory (address, writer.Contents);
		}
#endregion

#region IDisposable implementation
		private bool disposed = false;

		protected void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Process");
		}

		protected virtual void DoDispose ()
		{
			if (engine != null) {
				engine.Dispose ();
				engine = null;
			}
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

		~Process ()
		{
			Dispose (false);
		}
	}
}
