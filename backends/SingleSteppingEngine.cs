using GLib;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Diagnostics;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Architecture;
using Mono.CSharp.Debugger;

namespace Mono.Debugger.Backends
{
	public delegate bool BreakpointHitHandler (StackFrame frame, int index, object user_data);

	public class SingleSteppingEngine
	{
		public SingleSteppingEngine (DebuggerBackend backend, IInferior inferior, bool native)
		{
			this.backend = backend;
			this.symtab_manager = backend.SymbolTableManager;
			this.inferior = inferior;
			this.arch = inferior.Architecture;
			this.disassembler = inferior.Disassembler;
			this.native = native;

			inferior.SingleSteppingEngine = this;
			inferior.TargetExited += new TargetExitedHandler (child_exited);
			inferior.ChildEvent += new ChildEventHandler (child_event);

			symtab_manager.SymbolTableChangedEvent +=
				new SymbolTableManager.SymbolTableHandler (update_symtabs);
		}

		void update_symtabs (object sender, ISymbolTable symbol_table)
		{
			disassembler.SymbolTable = symbol_table;
			current_symtab = symbol_table;
			if (State == TargetState.STOPPED) {
				frames_invalid ();
				current_method = null;
				must_send_update = true;
				frame_changed (inferior.CurrentFrame, 0);
			}
		}

		public IMethod Lookup (TargetAddress address)
		{
			if (current_symtab == null)
				return null;

			return current_symtab.Lookup (address);
		}

		public event StateChangedHandler StateChangedEvent;
		public event MethodInvalidHandler MethodInvalidEvent;
		public event MethodChangedHandler MethodChangedEvent;
		public event StackFrameHandler FrameChangedEvent;
		public event StackFrameInvalidHandler FramesInvalidEvent;

		public TargetState State {
			get {
				return target_state;
			}
		}

		public StackFrame CurrentFrame {
			get {
				check_stopped ();
				return current_frame;
			}
		}

		public StackFrame[] GetBacktrace ()
		{
			check_stopped ();

			if (current_backtrace != null)
				return current_backtrace;

			backend.UpdateSymbolTable ();

			IInferiorStackFrame[] frames = inferior.GetBacktrace (-1, main_method_retaddr);
			current_backtrace = new StackFrame [frames.Length];

			for (int i = 0; i < frames.Length; i++) {
				TargetAddress address = frames [i].Address;

				IMethod method = Lookup (address);
				if ((method != null) && method.HasSource) {
					SourceLocation source = method.Source.Lookup (address);
					current_backtrace [i] = new StackFrame (
						inferior, address, frames [i], i, source, method);
				} else
					current_backtrace [i] = new StackFrame (
						inferior, address, frames [i], i);
			}

			return current_backtrace;
		}

		public IMethod CurrentMethod {
			get {
				check_stopped ();
				if (current_method == null)
					throw new NoMethodException ();
				return current_method;
			}
		}

		IInferior inferior;
		IArchitecture arch;
		DebuggerBackend backend;
		IDisassembler disassembler;
		SymbolTableManager symtab_manager;
		ISymbolTable current_symtab;
		bool native;

		TargetAddress main_method_retaddr = TargetAddress.Null;
		TargetState target_state = TargetState.NO_TARGET;

		TargetState change_target_state (TargetState new_state)
		{
			return change_target_state (new_state, 0);
		}

		TargetState change_target_state (TargetState new_state, int arg)
		{
			TargetState istate = inferior != null ? inferior.State : TargetState.NO_TARGET;
			if (new_state == target_state)
				return target_state;

			TargetState old_state = target_state;
			target_state = new_state;

			if (StateChangedEvent != null)
				StateChangedEvent (target_state, arg);

			return old_state;
		}

		void check_disposed ()
		{ }

		void check_inferior ()
		{
			if (inferior == null)
				throw new NoTargetException ();
		}

		void check_stopped ()
		{
			check_inferior ();

			if ((State != TargetState.STOPPED) && (State != TargetState.CORE_FILE))
				throw new TargetNotStoppedException ();
		}

		void check_can_run ()
		{
			check_inferior ();

			if (State != TargetState.STOPPED)
				throw new TargetNotStoppedException ();
		}

		bool start_native ()
		{
			if (!native)
				return false;

			TargetAddress main = inferior.MainMethodAddress;
			if (main.IsNull)
				return false;

			insert_temporary_breakpoint (main);
			return true;
		}

		void child_exited ()
		{
			inferior = null;
			frames_invalid ();
			if (FramesInvalidEvent != null)
				FramesInvalidEvent ();
		}

		bool initialized;
		bool reached_main;
		bool debugger_info_read;
		StepFrame current_step_frame = null;

		private enum StepOperation {
			None,
			Native,
			Run,
			StepLine,
			NextLine,
			StepFrame
		}

		StepOperation current_operation = StepOperation.None;
		StepFrame current_operation_frame = null;
		bool must_continue = false;

		bool child_breakpoint (int breakpoint)
		{
			if (breakpoint == 0)
				return backend.BreakpointHit (inferior.CurrentFrame);

			if (!breakpoints.Contains (breakpoint))
				return true;

			BreakpointHandle handle = (BreakpointHandle) breakpoints [breakpoint];
			StackFrame frame = null;
			if (handle.NeedsFrame)
				frame = get_frame (inferior.CurrentFrame);
			return handle.Handler (frame, breakpoint, handle.UserData);
		}

		void child_event (ChildEventType message, int arg)
		{
			if (inferior == null) {
				change_target_state (TargetState.EXITED, arg);
				return;
			}

			if (((message == ChildEventType.CHILD_STOPPED) ||
			     (message == ChildEventType.CHILD_HIT_BREAKPOINT)))
				inferior.DisableAllBreakpoints ();

			if (must_continue && (message == ChildEventType.CHILD_STOPPED)) {
				must_continue = false;
				do_continue (false);
				return;
			}

			if (temp_breakpoint_id != 0) {
				if ((message == ChildEventType.CHILD_EXITED) ||
				    (message == ChildEventType.CHILD_SIGNALED))
					temp_breakpoint_id = 0;
				else if ((arg == temp_breakpoint_id) || child_breakpoint (arg)) {
					inferior.RemoveBreakpoint (temp_breakpoint_id);
					temp_breakpoint_id = 0;
					if (message == ChildEventType.CHILD_HIT_BREAKPOINT) {
						child_event (ChildEventType.CHILD_STOPPED, 0);
						return;
					}
				} else {
					do_continue (true);
					return;
				}
			}

			if ((message == ChildEventType.CHILD_STOPPED) && (arg != 0)) {
				frame_changed (inferior.CurrentFrame, arg);
				return;
			}

			if (initialized && !reached_main &&
			    ((message == ChildEventType.CHILD_STOPPED) ||
			     (message == ChildEventType.CHILD_HIT_BREAKPOINT))) {
				reached_main = true;
				main_method_retaddr = inferior.GetReturnAddress ();
				frames_invalid ();
				backend.ReachedMain ();
			}

			switch (message) {
			case ChildEventType.CHILD_STOPPED: {
				TargetAddress frame = inferior.CurrentFrame;

				if (!initialized) {
					initialized = true;
					if (!native || start_native ()) {
						current_operation = StepOperation.Run;
						do_continue (false);
						break;
					}
				} else if (current_step_frame != null) {
					if ((frame >= current_step_frame.Start) &&
					    (frame < current_step_frame.End)) {
						try {
							Step (current_step_frame);
						} catch (Exception e) {
							Console.WriteLine ("EXCEPTION: " + e.ToString ());
						}
						break;
					}
					current_step_frame = null;
				}
				if (frame == main_method_retaddr) {
					do_continue (false);
					break;
				}

				frame_changed (frame, arg);
				break;
			}

			case ChildEventType.CHILD_EXITED:
			case ChildEventType.CHILD_SIGNALED:
				change_target_state (TargetState.EXITED, arg);
				break;

			case ChildEventType.CHILD_HIT_BREAKPOINT:
				if (child_breakpoint (arg))
					frame_changed (inferior.CurrentFrame, 0);
				else
					do_continue (true);
				break;

			case ChildEventType.CHILD_MEMORY_CHANGED:
				backend.Reload ();
				break;

			default:
				break;
			}
		}

		IMethod current_method;
		StackFrame current_frame;
		StackFrame[] current_backtrace;
		bool must_send_update;

		StackFrame get_frame (TargetAddress address)
		{
			if ((current_method == null) ||
			    (!MethodBase.IsInSameMethod (current_method, address))) {
				backend.UpdateSymbolTable ();
				current_method = Lookup (address);
			}

			// If some clown requested a backtrace while doing the symbol lookup ....
			frames_invalid ();

			IInferiorStackFrame[] frames = inferior.GetBacktrace (1, TargetAddress.Null);

			if ((current_method != null) && current_method.HasSource) {
				SourceLocation source = current_method.Source.Lookup (address);
				ILanguageBackend language = current_method.Module.Language;

				current_frame = new StackFrame (
					inferior, address, frames [0], 0, source, current_method);
			} else
				current_frame = new StackFrame (
					inferior, address, frames [0], 0);

			must_send_update = true;
			return current_frame;
		}

		bool is_in_step_frame (StepFrame frame, TargetAddress address)
                {
			if (address.IsNull || frame.Start.IsNull)
				return false;

                        if ((address < frame.Start) || (address >= frame.End))
                                return false;

                        return true;
                }

		void frame_changed (TargetAddress address, int arg)
		{
			IMethod old_method = current_method;

			if ((current_operation != StepOperation.None) &&
			    (current_operation != StepOperation.Native) &&
			    (current_operation_frame != null) &&
			    is_in_step_frame (current_operation_frame, address)) {
				Step (current_operation_frame);
				return;
			}

			if (!must_send_update && (current_frame != null) &&
			    current_frame.IsValid && (current_frame.TargetAddress == address)) {
				current_operation = StepOperation.None;
				change_target_state (TargetState.STOPPED, arg);
				return;
			}

			frames_invalid ();

			if ((current_method == null) ||
			    (!MethodBase.IsInSameMethod (current_method, address))) {
				backend.UpdateSymbolTable ();
				current_method = Lookup (address);
			}

			// If some clown requested a backtrace while doing the symbol lookup ....
			frames_invalid ();

			IInferiorStackFrame[] frames = inferior.GetBacktrace (1, TargetAddress.Null);

			if ((current_method != null) && current_method.HasSource) {
				SourceLocation source = current_method.Source.Lookup (address);
				ILanguageBackend language = current_method.Module.Language;

				if (check_method_operation (address, current_method, source)) {
					must_send_update = true;
					return;
				}

				current_frame = new StackFrame (
					inferior, address, frames [0], 0, source, current_method);
			} else
				current_frame = new StackFrame (
					inferior, address, frames [0], 0);

			current_operation = StepOperation.None;

			change_target_state (TargetState.STOPPED, arg);

			if (must_send_update || (current_method != old_method)) {
				if (current_method != null) {
					if (MethodChangedEvent != null)
						MethodChangedEvent (current_method);
				} else {
					if (MethodInvalidEvent != null)
						MethodInvalidEvent ();
				}
			}

			must_send_update = false;

			if (FrameChangedEvent != null)
				FrameChangedEvent (current_frame);

			return;
		}

		bool check_method_operation (TargetAddress address, IMethod method, SourceLocation source)
		{
			ILanguageBackend language = method.Module.Language;
			if (language == null)
				return false;

			if ((current_operation != StepOperation.StepLine) &&
			    (current_operation != StepOperation.NextLine) &&
			    (current_operation != StepOperation.Run))
				return false;

			if ((source.SourceOffset > 0) && (source.SourceRange > 0)) {
				start_step_operation (StepOperation.Native, new StepFrame (
					address - source.SourceOffset, address + source.SourceRange,
					language, current_operation == StepOperation.StepLine ?
					StepMode.StepFrame : StepMode.Finish));
				return true;
			} else if (method.HasMethodBounds && (address < method.MethodStartAddress)) {
				start_step_operation (StepOperation.Native, new StepFrame (
					method.StartAddress, method.MethodStartAddress,
					null, StepMode.Finish));
				return true;
			}

			return false;
		}

		void frames_invalid ()
		{
			if (current_frame != null) {
				current_frame.Dispose ();
				current_frame = null;
			}

			if (current_backtrace != null) {
				foreach (StackFrame frame in current_backtrace)
					frame.Dispose ();
				current_backtrace = null;
			}
		}

		int temp_breakpoint_id = 0;
		void insert_temporary_breakpoint (TargetAddress address)
		{
			check_inferior ();
			temp_breakpoint_id = inferior.InsertBreakpoint (address);
		}

		void set_step_frame (StepFrame frame)
		{
			if (frame != null) {
				switch (frame.Mode) {
				case StepMode.StepFrame:
				case StepMode.NativeStepFrame:
				case StepMode.Finish:
					current_step_frame = frame;
					break;

				default:
					current_step_frame = null;
					break;
				}
			} else
				current_step_frame = null;
		}

		void do_step (StepFrame frame)
		{
			check_inferior ();
			set_step_frame (frame);

			TargetState old_state = change_target_state (TargetState.RUNNING);
			try {
				if (!inferior.CurrentInstructionIsBreakpoint)
					inferior.EnableAllBreakpoints ();
				inferior.Step ();
			} catch {
				change_target_state (old_state);
			}
		}

		void do_next ()
		{
			check_inferior ();
			TargetAddress address = inferior.CurrentFrame;
			if (arch.IsRetInstruction (address)) {
				do_step (null);
				return;
			}

			address += disassembler.GetInstructionSize (address);

			insert_temporary_breakpoint (address);
			do_continue (false);
		}

		void do_continue (bool is_breakpoint)
		{
			check_inferior ();

			TargetState old_state = change_target_state (TargetState.RUNNING);
			try {
				if (is_breakpoint || inferior.CurrentInstructionIsBreakpoint) {
					must_continue = true;
					inferior.Step ();
				} else {
					inferior.EnableAllBreakpoints ();
					inferior.Continue ();
				}
			} catch {
				change_target_state (old_state);
			}
		}

		protected void Step (StepFrame frame)
		{
			check_inferior ();

			/*
			 * If no step frame is given, just step one machine instruction.
			 */
			if (frame == null) {
				do_step (null);
				return;
			}

			/*
			 * Step one instruction, but step over function calls.
			 */
			if (frame.Mode == StepMode.NextInstruction) {
				do_next ();
				return;
			}

			TargetAddress current_frame = inferior.CurrentFrame;

			/*
			 * If this is not a call instruction, continue stepping until we leave
			 * the specified step frame.
			 */
			int insn_size;
			TargetAddress call = arch.GetCallTarget (current_frame, out insn_size);
			if (call.IsNull) {
				do_step (frame);
				return;
			}

			/*
			 * If we have a source language, check for trampolines.
			 * This will trigger a JIT compilation if neccessary.
			 */
			if ((frame.Mode != StepMode.Finish) && (frame.Language != null)) {
				TargetAddress trampoline = frame.Language.GetTrampoline (call);

				/*
				 * If this is a trampoline, insert a breakpoint at the start of
				 * the corresponding method and continue.
				 *
				 * We don't need to distinguish between StepMode.SingleInstruction
				 * and StepMode.StepFrame here since we'd leave the step frame anyways
				 * when entering the method.
				 */
				if (!trampoline.IsNull) {
					IMethod tmethod = null;
					if (current_symtab != null) {
						backend.UpdateSymbolTable ();
						tmethod = Lookup (trampoline);
					}
					if ((tmethod == null) || !tmethod.Module.StepInto) {
						set_step_frame (frame);
						do_next ();
						return;
					}

					insert_temporary_breakpoint (trampoline);
					do_continue (false);
					return;
				}
			}

			/*
			 * When StepMode.SingleInstruction was requested, enter the method no matter
			 * whether it's a system function or not.
			 */
			if (frame.Mode == StepMode.SingleInstruction) {
				do_step (null);
				return;
			}

			/*
			 * In StepMode.Finish, always step over all methods.
			 */
			if (frame.Mode == StepMode.Finish) {
				current_step_frame = frame;
				do_next ();
				return;
			}

			/*
			 * Try to find out whether this is a system function by doing a symbol lookup.
			 * If it can't be found in the symbol tables, assume it's a system function
			 * and step over it.
			 */
			IMethod method = Lookup (call);
			if (current_symtab != null)
				method = current_symtab.Lookup (call);
			if ((method == null) || !method.Module.StepInto) {
				set_step_frame (frame);
				do_next ();
				return;
			}

			/*
			 * Finally, step into the method.
			 */
			do_step (null);
		}

		public void Continue (TargetAddress until)
		{
			check_inferior ();
			insert_temporary_breakpoint (until);
			Continue ();
		}

		public void Continue ()
		{
			check_inferior ();
			current_operation = StepOperation.Run;
			do_continue (false);
		}

		StepFrame get_step_frame ()
		{
			check_inferior ();
			StackFrame frame = CurrentFrame;
			ILanguageBackend language = (frame.Method != null) ?
				frame.Method.Module.Language : null;

			if (frame.SourceLocation == null)
				return null;

			int offset = frame.SourceLocation.SourceOffset;
			int range = frame.SourceLocation.SourceRange;

			TargetAddress start = frame.TargetAddress - offset;
			TargetAddress end = frame.TargetAddress + range;

			return new StepFrame (start, end, language, StepMode.StepFrame);
		}

		StepFrame get_simple_step_frame (StepMode mode)
		{
			check_inferior ();
			StackFrame frame = CurrentFrame;
			ILanguageBackend language = (frame.Method != null) ?
				frame.Method.Module.Language : null;

			return new StepFrame (language, mode);
		}

		void start_step_operation (StepOperation operation, StepFrame frame)
		{
			current_operation = operation;
			current_operation_frame = frame;
			Step (frame);
		}

		void start_step_operation (StepMode mode)
		{
			start_step_operation (StepOperation.Native, get_simple_step_frame (mode));
		}			

		public void StepInstruction ()
		{
			check_can_run ();
			start_step_operation (StepMode.SingleInstruction);
		}

		public void NextInstruction ()
		{
			check_can_run ();
			start_step_operation (StepMode.NextInstruction);
		}

		public void StepLine ()
		{
			check_can_run ();
			start_step_operation (StepOperation.StepLine, get_step_frame ());
		}

		public void NextLine ()
		{
			check_can_run ();
			StepFrame frame = get_step_frame ();
			if (frame == null) {
				start_step_operation (StepMode.NextInstruction);
				return;
			}

			start_step_operation (
				StepOperation.NextLine,
				new StepFrame (frame.Start, frame.End, null, StepMode.Finish));
		}

		public void Finish ()
		{
			check_can_run ();
			StackFrame frame = CurrentFrame;
			if (frame.Method == null)
				throw new NoMethodException ();

			start_step_operation (StepOperation.StepFrame, new StepFrame (
				frame.Method.StartAddress, frame.Method.EndAddress, null, StepMode.Finish));
		}

		Hashtable breakpoints = new Hashtable ();

		public int InsertBreakpoint (TargetAddress address, BreakpointHitHandler handler,
					     bool needs_frame, object user_data)
		{
			check_inferior ();
			int index = inferior.InsertBreakpoint (address);
			breakpoints.Add (index, new BreakpointHandle (index, handler, needs_frame, user_data));
			return index;
		}

		public void RemoveBreakpoint (int index)
		{
			check_disposed ();
			if (inferior != null)
				inferior.RemoveBreakpoint (index);
			breakpoints.Remove (index);
		}

		private struct BreakpointHandle
		{
			public readonly int Index;
			public readonly bool NeedsFrame;
			public readonly BreakpointHitHandler Handler;
			public readonly object UserData;

			public BreakpointHandle (int index, BreakpointHitHandler handler,
						 bool needs_frame, object user_data)
			{
				this.Index = index;
				this.Handler = handler;
				this.NeedsFrame = needs_frame;
				this.UserData = user_data;
			}
		}
	}
}
