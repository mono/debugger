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
	public delegate bool BreakpointCheckHandler (StackFrame frame, int index, object user_data);
	public delegate void BreakpointHitHandler (StackFrame frame, int index, object user_data);
	public delegate void ProcessExitedHandler (Process process);

	public class Process : ITargetAccess, IDisassembler
	{
		internal Process (SingleSteppingEngine engine, ProcessStart start,
				  Inferior inferior)
		{
			this.engine = engine;
			this.id = ++next_id;

			inferior.TargetOutput += new TargetOutputHandler (OnInferiorOutput);
			inferior.DebuggerOutput += new DebuggerOutputHandler (OnDebuggerOutput);
			inferior.DebuggerError += new DebuggerErrorHandler (OnDebuggerError);
		}

		int id;
		static int next_id = 0;
		SingleSteppingEngine engine;

		protected ILanguage NativeLanguage {
			get {
				check_engine ();
				return engine.NativeLanguage;
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

		protected virtual void OnInferiorOutput (bool is_stderr, string line)
		{
			if (TargetOutput != null)
				TargetOutput (is_stderr, line);
		}

		protected virtual void OnDebuggerOutput (string line)
		{
			if (DebuggerOutput != null)
				DebuggerOutput (line);
		}

		protected virtual void OnDebuggerError (object sender, string message,
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

		void check_engine ()
		{
			if (engine == null)
				throw new NoTargetException ();
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
			CommandResult result = engine.SendSyncCommand (CommandType.GetBacktrace, max_frames);
			if (result.Type == CommandResultType.CommandOk) {
				return (Backtrace) result.Data;
			} else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		public Backtrace GetBacktrace ()
		{
			Backtrace bt = engine.CurrentBacktrace;
			if (bt != null)
				return bt;

			return GetBacktrace (-1);
		}

		public Register GetRegister (int index)
		{
			foreach (Register register in GetRegisters ()) {
				if (register.Index == index)
					return register;
			}

			throw new NoSuchRegisterException ();
		}

		public Register[] GetRegisters ()
		{
			CommandResult result = engine.SendSyncCommand (CommandType.GetRegisters, null);
			if (result.Type == CommandResultType.CommandOk) {
				return (Register []) result.Data;
			} else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		public void SetRegister (int register, long value)
		{
			Register reg = new Register (register, value);
			engine.SendSyncCommand (CommandType.SetRegister, reg);
		}

		public void SetRegisters (int[] registers, long[] values)
		{
			throw new NotImplementedException ();
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			check_engine ();
			return engine.GetMemoryMaps ();
		}

		protected string SimpleLookup (TargetAddress address, bool exact_match)
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

		bool start_step_operation (Operation operation, bool wait)
		{
			check_engine ();
			if (!engine.StartOperation ())
				return false;
			engine.SendAsyncCommand (new Command (engine, operation), wait);
			return true;
		}

		bool start_step_operation (OperationType operation, TargetAddress until,
					   bool wait)
		{
			return start_step_operation (new Operation (operation, until), wait);
		}

		bool start_step_operation (OperationType operation, bool wait)
		{
			return start_step_operation (new Operation (operation), wait);
		}

		void call_method (CallMethodData cdata)
		{
			engine.SendCallbackCommand (new Command (engine, new Operation (cdata)));
		}

		void call_method (RuntimeInvokeData rdata)
		{
			engine.SendCallbackCommand (new Command (engine, new Operation (rdata)));
		}

		// <summary>
		//   Step one machine instruction, but don't step into trampolines.
		// </summary>
		public bool StepInstruction (bool wait)
		{
			return start_step_operation (OperationType.StepInstruction, wait);
		}

		// <summary>
		//   Step one machine instruction, always step into method calls.
		// </summary>
		public bool StepNativeInstruction (bool wait)
		{
			return start_step_operation (OperationType.StepNativeInstruction, wait);
		}

		// <summary>
		//   Step one machine instruction, but step over method calls.
		// </summary>
		public bool NextInstruction (bool wait)
		{
			return start_step_operation (OperationType.NextInstruction, wait);
		}

		// <summary>
		//   Step one source line.
		// </summary>
		public bool StepLine (bool wait)
		{
			return start_step_operation (OperationType.StepLine, wait);
		}

		// <summary>
		//   Step one source line, but step over method calls.
		// </summary>
		public bool NextLine (bool wait)
		{
			return start_step_operation (OperationType.NextLine, wait);
		}

		// <summary>
		//   Continue until leaving the current method.
		// </summary>
		public bool Finish (bool wait)
		{
			check_engine ();
			if (!engine.StartOperation ())
				return false;

			StackFrame frame = CurrentFrame;
			if (frame.Method == null) {
				engine.AbortOperation ();
				throw new NoMethodException ();
			}

			StepFrame sf = new StepFrame (
				frame.Method.StartAddress, frame.Method.EndAddress,
				null, StepMode.Finish);

			Operation operation = new Operation (OperationType.StepFrame, sf);
			engine.SendAsyncCommand (new Command (engine, operation), wait);
			return true;
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
			if (in_background)
				return start_step_operation (OperationType.RunInBackground,
							     until, wait);
			else
				return start_step_operation (OperationType.Run, until, wait);
		}

		public void Kill ()
		{
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
		//   Insert a breakpoint at address @address.  Each time this breakpoint
		//   is hit, @handler will be called and @user_data will be passed to it
		//   as argument.  @needs_frame specifies whether the @handler needs the
		//   StackFrame argument.
		//
		//   Returns a number which may be passed to RemoveBreakpoint() to remove
		//   the breakpoint.
		// </summary>
		public int InsertBreakpoint (BreakpointHandle handle,
						      TargetAddress address,
						      BreakpointCheckHandler check_handler,
						      BreakpointHitHandler hit_handler,
						      bool needs_frame, object user_data)
		{
			check_engine ();

			BreakpointManager.Handle data = new BreakpointManager.Handle (
				address, handle, check_handler, hit_handler, needs_frame, user_data);

			CommandResult result = engine.SendSyncCommand (
				CommandType.InsertBreakpoint, data);
			if (result.Type != CommandResultType.CommandOk)
				throw new Exception ();

			return (int) result.Data;
		}

		// <summary>
		//   Remove breakpoint @index.  @index is the breakpoint number which has
		//   been returned by InsertBreakpoint().
		// </summary>
		public void RemoveBreakpoint (int index)
		{
			check_disposed ();
			if (engine != null)
				engine.SendSyncCommand (CommandType.RemoveBreakpoint, index);
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
			CommandResult result = engine.SendSyncCommand (CommandType.GetInstructionSize, address);
			if (result.Type == CommandResultType.CommandOk) {
				return (int) result.Data;
			} else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		public AssemblerLine DisassembleInstruction (IMethod method, TargetAddress address)
		{
			check_engine ();
			CommandResult result = engine.SendSyncCommand (CommandType.DisassembleInstruction, method, address);
			if (result.Type == CommandResultType.CommandOk) {
				return (AssemblerLine) result.Data;
			} else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				return null;
		}

		public AssemblerMethod DisassembleMethod (IMethod method)
		{
			check_engine ();
			CommandResult result = engine.SendSyncCommand (CommandType.DisassembleMethod, method);
			if (result.Type == CommandResultType.CommandOk)
				return (AssemblerMethod) result.Data;
			else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		public long CallMethod (TargetAddress method, long method_argument,
					string string_argument)
		{
			CallMethodData data = new CallMethodData (
				method, method_argument, string_argument, null);

			call_method (data);
			if (data.Result == null)
				throw new Exception ();
			return (long) data.Result;
		}

		public TargetAddress CallMethod (TargetAddress method, string arg)
		{
			CallMethodData data = new CallMethodData (method, 0, arg, null);

			call_method (data);
			if (data.Result == null)
				throw new Exception ();
			long retval = (long) data.Result;
			if (engine.TargetAddressSize == 4)
				retval &= 0xffffffffL;
			return new TargetAddress (engine.AddressDomain, retval);
		}

		public TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
						 TargetAddress arg2)
		{
			CallMethodData data = new CallMethodData (
				method, arg1.Address, arg2.Address, null);

			call_method (data);
			if (data.Result == null)
				throw new Exception ();

			long retval = (long) data.Result;
			if (engine.TargetAddressSize == 4)
				retval &= 0xffffffffL;
			return new TargetAddress (engine.AddressDomain, retval);
		}

		internal bool RuntimeInvoke (ILanguageBackend language,
					     TargetAddress method_argument,
					     TargetAddress object_argument,
					     TargetAddress[] param_objects)
		{
			RuntimeInvokeData data = new RuntimeInvokeData (
				language, method_argument, object_argument, param_objects);
			return start_step_operation (new Operation (data), true);
		}

		internal TargetAddress RuntimeInvoke (ILanguageBackend language,
						      TargetAddress method_argument,
						      TargetAddress object_argument,
						      TargetAddress[] param_objects,
						      out TargetAddress exc_object)
		{
			RuntimeInvokeData data = new RuntimeInvokeData (
				language, method_argument, object_argument, param_objects);

			call_method (data);
			if (!data.InvokeOk)
				throw new Exception ();

			exc_object = data.ExceptionObject;
			return data.ReturnObject;
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

		//
		// ITargetInfo
		//

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

		//
		// ITargetMemoryAccess
		//

		protected byte[] read_memory (TargetAddress address, int size)
		{
			CommandResult result = engine.SendSyncCommand (CommandType.ReadMemory, address, size);
			if (result.Type == CommandResultType.CommandOk)
				return (byte []) result.Data;
			else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		string read_string (TargetAddress address)
		{
			CommandResult result = engine.SendSyncCommand (CommandType.ReadString, address);
			if (result.Type == CommandResultType.CommandOk)
				return (string) result.Data;
			else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
		}

		ITargetMemoryReader get_memory_reader (TargetAddress address, int size)
		{
			byte[] buffer = read_memory (address, size);
			return new TargetReader (buffer, this);
		}

		protected void write_memory (TargetAddress address, byte[] buffer)
		{
			CommandResult result = engine.SendSyncCommand (CommandType.WriteMemory, address, buffer);
			if (result.Type == CommandResultType.CommandOk)
				return;
			else if (result.Type == CommandResultType.Exception)
				throw (Exception) result.Data;
			else
				throw new InternalError ();
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
			ITargetMemoryReader reader = get_memory_reader (address, engine.TargetIntegerSize);
			return reader.ReadLongInteger ();
		}

		TargetAddress ITargetMemoryAccess.ReadAddress (TargetAddress address)
		{
			check_engine ();
			ITargetMemoryReader reader = get_memory_reader (address, engine.TargetIntegerSize);
			return reader.ReadAddress ();
		}

		TargetAddress ITargetMemoryAccess.ReadGlobalAddress (TargetAddress address)
		{
			check_engine ();
			ITargetMemoryReader reader = get_memory_reader (address, engine.TargetIntegerSize);
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


		//
		// Stack frames.
		//

		internal StackFrame CreateFrame (TargetAddress address, int level,
						 Backtrace bt, SourceAddress source,
						 IMethod method)
		{
			if (source != null)
				return new MyStackFrame (this, address, level,
							 bt, source, method);
			else
				return new MyStackFrame (this, address, level, bt);
		}


		//
		// IDisposable
		//

		private bool disposed = false;

		protected void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Process");
		}

		protected virtual void DoDispose ()
		{
			engine.Dispose ();
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

		~Process ()
		{
			Dispose (false);
		}

		protected class MyStackFrame : StackFrame
		{
			Process process;
			Backtrace backtrace;
			ILanguage language;
			ILanguageBackend lbackend;

			Register[] registers;
			bool has_registers;

			internal MyStackFrame (Process process, TargetAddress address,
					       int level, Backtrace backtrace,
					       SourceAddress source, IMethod method)
				: base (address, level, source, method)
			{
				this.process = process;
				this.backtrace = backtrace;
				this.language = method.Module.Language;
				this.lbackend = method.Module.LanguageBackend as ILanguageBackend;
			}

			internal MyStackFrame (Process process, TargetAddress address,
					       int level, Backtrace backtrace)
				: base (address, level, process.SimpleLookup (address, false))
			{
				this.process = process;
				this.backtrace = backtrace;
				this.language = process.NativeLanguage;
			}

			public override ITargetAccess TargetAccess {
				get { return process; }
			}

			public override Register[] Registers {
				get {
					if (has_registers)
						return registers;

					if (backtrace == null) {
						registers = process.GetRegisters ();
						has_registers = true;
					} else {
						registers = backtrace.UnwindStack (Level);
						has_registers = true;
					}

					return registers;
				}
			}

			public override TargetLocation GetRegisterLocation (int index, long reg_offset, bool dereference, long offset)
			{
				return new MonoVariableLocation (this, dereference, index, reg_offset, false, offset);
			}

			public override void SetRegister (int index, long value)
			{
				if (backtrace != null)
					throw new NotImplementedException ();

				process.SetRegister (index, value);

				has_registers = false;
				registers = null;
			}

			public override ILanguage Language {
				get {
					return language;
				}
			}

			protected override AssemblerLine DoDisassembleInstruction (TargetAddress address)
			{
				return process.DisassembleInstruction (Method, address);
			}

			public override AssemblerMethod DisassembleMethod ()
			{
				if (Method == null)
					throw new NoMethodException ();

				return process.DisassembleMethod (Method);
			}

			public override TargetAddress CallMethod (TargetAddress method,
								  TargetAddress arg1,
								  TargetAddress arg2)
			{
				return process.CallMethod (method, arg1, arg2);
			}

			public override TargetAddress CallMethod (TargetAddress method,
								  string arg)
			{
				return process.CallMethod (method, arg);
			}

			public override bool RuntimeInvoke (TargetAddress method_argument,
							    TargetAddress object_argument,
							    TargetAddress[] param_objects)
			{
				if (lbackend == null)
					throw new InvalidOperationException ();

				return process.RuntimeInvoke (lbackend, method_argument,
							      object_argument, param_objects);
			}

			public override TargetAddress RuntimeInvoke (TargetAddress method_arg,
								     TargetAddress object_arg,
								     TargetAddress[] param,
								     out TargetAddress exc_obj)
			{
				if (lbackend == null)
					throw new InvalidOperationException ();

				return process.RuntimeInvoke (lbackend, method_arg,
							      object_arg, param, out exc_obj);
			}
		}
	}
}
