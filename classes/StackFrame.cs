using System;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger.Languages;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	[Serializable]
	public sealed class Register
	{
		public readonly string Name;
		public readonly int Index;
		public readonly int Size;
		TargetAddress addr_on_stack;
		Registers registers;
		bool valid;
		long value;

		public Register (Registers registers, string name, int index, int size,
				 bool valid, long value)
		{
			this.registers = registers;
			this.Name = name;
			this.Index = index;
			this.Size = size;
			this.valid = valid;
			this.value = value;
			this.addr_on_stack = TargetAddress.Null;
		}

		public long Value {
			get {
				if (!valid)
					throw new InvalidOperationException ();

				return value;
			}
		}

		public long GetValue ()
		{
			return value;
		}

		public void SetValue (TargetAddress address, long value)
		{
			this.valid = true;
			this.addr_on_stack = address;
			this.value = value;
		}

		public void SetValue (TargetAddress address, TargetAddress value)
		{
			this.valid = true;
			this.addr_on_stack = address;
			this.value = value.Address;
		}

		public void SetValue (long value)
		{
			this.valid = true;
			this.addr_on_stack = TargetAddress.Null;
			this.value = value;
		}

		public void SetValue (TargetAddress value)
		{
			this.valid = true;
			this.addr_on_stack = TargetAddress.Null;
			this.value = value.Address;
		}

		public void WriteRegister (Thread target, long value)
		{
			this.value = value;

			if (addr_on_stack.IsNull)
				target.SetRegisters (registers);
			else if (Size == target.TargetInfo.TargetIntegerSize)
				target.WriteInteger (addr_on_stack, (int) value);
			else
				target.WriteLongInteger (addr_on_stack, value);
		}

		public bool Valid {
			get {
				return valid;
			}

			set {
				valid = value;
			}
		}

		public TargetAddress AddressOnStack {
			get {
				return addr_on_stack;
			}
		}

		public override string ToString ()
		{
			return String.Format ("Register ({0}:{1}:{2:x})",
					      Index, Valid, GetValue ());
		}
	}

	[Serializable]
	public sealed class Registers
	{
		Register[] regs;
		bool from_current_frame;

		internal Registers (Architecture arch)
		{
			regs = new Register [arch.CountRegisters];
			for (int i = 0; i < regs.Length; i++)
				regs [i] = new Register (
					this, arch.RegisterNames [i], i,
					arch.RegisterSizes [i], false, 0);
		}

		internal Registers (Architecture arch, long[] values)
		{
			regs = new Register [arch.CountRegisters];
			if (regs.Length != values.Length)
				throw new ArgumentException ();
			for (int i = 0; i < regs.Length; i++)
				regs [i] = new Register (
					this, arch.RegisterNames [i], i,
					arch.RegisterSizes [i], true, values [i]);
			from_current_frame = true;
		}

		internal Registers (Registers old_regs)
		{
			regs = new Register [old_regs.regs.Length];
			for (int i = 0; i < regs.Length; i++)
				regs [i] = new Register (
					this, old_regs [i].Name, i, old_regs [i].Size,
					false, old_regs [i].GetValue ());
		}

		public Register this [int index] {
			get {
				return regs [index];
			}
		}

		public Register this [string name] {
			get {
				foreach (Register reg in regs)
					if (reg.Name == name)
						return reg;

				return null;
			}
		}

		internal long[] Values {
			get {
				long[] retval = new long [regs.Length];
				for (int i = 0; i < regs.Length; i++)
					retval [i] = regs [i].GetValue ();

				return retval;
			}
		}

		public bool FromCurrentFrame {
			get {
				return from_current_frame;
			}
		}
	}

	[Serializable]
	public sealed class StackFrame : DebuggerMarshalByRefObject
	{
		protected readonly TargetAddress address;
		protected readonly TargetAddress stack_pointer;
		protected readonly TargetAddress frame_address;
		protected readonly Registers registers;

		int level;
		Method method;
		Thread thread;
		SourceAddress source;
		StackFrame parent_frame;
		TargetObject exc_object;
		bool has_source;
		Symbol name;

		internal StackFrame (Thread thread, TargetAddress address, TargetAddress stack_ptr,
				     TargetAddress frame_address, Registers registers)
		{
			this.thread = thread;
			this.address = address;
			this.stack_pointer = stack_ptr;
			this.frame_address = frame_address;
			this.registers = registers;
		}

		internal StackFrame (Thread thread, TargetAddress address, TargetAddress stack_ptr,
				     TargetAddress frame_address, Registers registers,
				     Symbol name)
			: this (thread, address, stack_ptr, frame_address, registers)
		{
			this.name = name;
		}

		internal StackFrame (Thread thread, TargetAddress address, TargetAddress stack_ptr,
				     TargetAddress frame_address, Registers registers,
				     Method method)
			: this (thread, address, stack_ptr, frame_address, registers)
		{
			this.method = method;
			this.name = new Symbol (method.Name, method.StartAddress, 0);
		}


		internal StackFrame (Thread thread, TargetAddress address, TargetAddress stack_ptr,
				     TargetAddress frame_address, Registers registers,
				     Method method, SourceAddress source)
			: this (thread, address, stack_ptr, frame_address, registers, method)
		{
			this.source = source;
			this.has_source = true;
		}

		public int Level {
			get { return level; }
		}

		internal void SetLevel (int new_level)
		{
			level = new_level;
		}

		public SourceAddress SourceAddress {
			get {
				if (has_source)
					return source;
				if ((method != null) && method.HasSource)
					source = method.Source.Lookup (address);
				has_source = true;
				return source;
			}
		}

		public TargetAddress TargetAddress {
			get { return address; }
		}

		public TargetAddress StackPointer {
			get { return stack_pointer; }
		}

		public TargetAddress FrameAddress {
			get { return frame_address; }
		}

		public Thread Thread {
			get { return thread; }
		}

		public Registers Registers {
			get { return registers; }
		}

		public Method Method {
			get { return method; }
		}

		public Symbol Name {
			get { return name; }
		}

		public TargetVariable[] Locals {
			get {
				ArrayList list = new ArrayList ();
				foreach (TargetVariable local in Method.Locals) {
					if (local.IsInScope (TargetAddress))
						list.Add (local);
				}
				TargetVariable[] retval = new TargetVariable [list.Count];
				list.CopyTo (retval, 0);
				return retval;
			}
		}

		public Language Language {
			get {
				if (method != null)
					return method.Module.Language;
				else
					return thread.NativeLanguage;
			}
		}

		internal StackFrame ParentFrame {
			get { return parent_frame; }
			set { parent_frame = value; }
		}

		public TargetObject ExceptionObject {
			get { return exc_object; }
		}

		internal void SetExceptionObject (TargetAddress exc)
		{
			try {
				Language lang = thread.ThreadServant.ProcessServant.MonoLanguage;
				if (lang != null)
					exc_object = lang.CreateObject (thread, exc);
			} catch {
				exc_object = null;
			}
		}

		internal StackFrame UnwindStack (TargetMemoryAccess memory)
		{
			if (parent_frame != null)
				return parent_frame;

			StackFrame new_frame = null;
			if (method != null) {
				try {
					new_frame = method.UnwindStack (this, memory);
				} catch (TargetException) {
				}

				if (new_frame != null)
					return new_frame;
			}

			foreach (Module module in thread.Process.Modules) {
				try {
					new_frame = module.UnwindStack (this, memory);
				} catch {
					continue;
				}
				if (new_frame != null)
					return new_frame;
			}

			return memory.Architecture.UnwindStack (this, memory, null, 0);
		}

		public override string ToString ()
		{
			StringBuilder sb = new StringBuilder ();

			sb.Append (String.Format ("#{0}: ", level));

			if (method != null) {
				sb.Append (String.Format ("{0} in {1}", address, method.Name));
				if (method.IsLoaded) {
					long offset = address - method.StartAddress;
					if (offset > 0)
						sb.Append (String.Format ("+0x{0:x}", offset));
					else if (offset < 0)
						sb.Append (String.Format ("-0x{0:x}", -offset));
				}
			} else if (name != null)
				sb.Append (String.Format ("{0} in {1}", address, name));
			else
				sb.Append (String.Format ("{0}", address));

			if (SourceAddress != null)
				sb.Append (String.Format (" at {0}", source.Name));

			return sb.ToString ();
		}
	}
}
