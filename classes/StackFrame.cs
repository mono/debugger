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
		public readonly int Index;
		public readonly int Size;
		TargetAddress addr_on_stack;
		Registers registers;
		bool valid;
		long value;

		public Register (Registers registers, int index, int size,
				 bool valid, long value)
		{
			this.registers = registers;
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

		public void WriteRegister (TargetAccess target, long value)
		{
			this.value = value;

			if (addr_on_stack.IsNull)
				target.TargetMemoryAccess.SetRegisters (registers);
			else if (Size == target.TargetMemoryInfo.TargetIntegerSize)
				target.TargetMemoryAccess.WriteInteger (addr_on_stack, (int) value);
			else
				target.TargetMemoryAccess.WriteLongInteger (addr_on_stack, value);
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

		public Registers (Architecture arch)
		{
			regs = new Register [arch.CountRegisters];
			for (int i = 0; i < regs.Length; i++)
				regs [i] = new Register (
					this, i, arch.RegisterSizes [i], false, 0);
		}

		public Registers (Architecture arch, long[] values)
		{
			regs = new Register [arch.CountRegisters];
			if (regs.Length != values.Length)
				throw new ArgumentException ();
			for (int i = 0; i < regs.Length; i++)
				regs [i] = new Register (
					this, i, arch.RegisterSizes [i], true, values [i]);
			from_current_frame = true;
		}

		public Registers (Registers old_regs)
		{
			regs = new Register [old_regs.regs.Length];
			for (int i = 0; i < regs.Length; i++)
				regs [i] = new Register (
					this, i, old_regs [i].Size, false,
					old_regs [i].GetValue ());
		}

		public Register this [int index] {
			get {
				return regs [index];
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
	public sealed class StackFrame : MarshalByRefObject
	{
		protected readonly TargetAddress address;
		protected readonly TargetAddress stack_pointer;
		protected readonly TargetAddress frame_address;
		protected readonly Registers registers;

		int level;
		Method method;
		Process process;
		TargetAccess target;
		SourceAddress source;
		StackFrame parent_frame;
		bool has_source;
		Symbol name;

		internal StackFrame (Process process, TargetAccess target,
				     TargetAddress address, TargetAddress stack_pointer,
				     TargetAddress frame_address, Registers registers)
		{
			this.process = process;
			this.target = target;
			this.address = address;
			this.stack_pointer = stack_pointer;
			this.frame_address = frame_address;
			this.registers = registers;
		}

		internal StackFrame (Process process, TargetAccess target,
				     TargetAddress address, TargetAddress stack_pointer,
				     TargetAddress frame_address, Registers registers,
				     Symbol name)
			: this (process, target, address, stack_pointer, frame_address,
				registers)
		{
			this.name = name;
		}

		internal StackFrame (Process process, TargetAccess target,
				     TargetAddress address, TargetAddress stack_pointer,
				     TargetAddress frame_address, Registers registers,
				     Method method)
			: this (process, target, address, stack_pointer, frame_address,
				registers)
		{
			this.method = method;
			this.name = new Symbol (method.Name, method.StartAddress, 0);
		}


		internal StackFrame (Process process, TargetAccess target,
				     TargetAddress address, TargetAddress stack_pointer,
				     TargetAddress frame_address, Registers registers,
				     Method method, SourceAddress source)
			: this (process, target, address, stack_pointer, frame_address,
				registers, method)
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

		public Process Process {
			get { return process; }
		}

		public TargetAccess TargetAccess {
			get { return target; }
		}

		public Registers Registers {
			get { return registers; }
		}

		public long GetRegister (int index)
		{
			return Registers [index].Value;
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
					if (local.IsAlive (TargetAddress))
						list.Add (local);
				}
				TargetVariable[] retval = new TargetVariable [list.Count];
				list.CopyTo (retval, 0);
				return retval;
			}
		}

		public void SetRegister (int index, long value)
		{
			Registers [index].WriteRegister (process.TargetAccess, value);
		}

		public Language Language {
			get {
				if (method != null)
					return method.Module.Language;
				else
					return process.NativeLanguage;
			}
		}

		internal StackFrame ParentFrame {
			get { return parent_frame; }
			set { parent_frame = value; }
		}

		public StackFrame UnwindStack (ITargetMemoryAccess memory, Architecture arch)
		{
			if (parent_frame != null)
				return parent_frame;

			StackFrame new_frame = null;
			if (method != null) {
				try {
					new_frame = method.UnwindStack (this, memory, arch);
				} catch (TargetException) {
				}

				if (new_frame != null)
					return new_frame;
			}

			foreach (Module module in process.Debugger.Modules) {
				try {
					new_frame = module.UnwindStack (this, memory);
				} catch {
					continue;
				}
				if (new_frame != null)
					return new_frame;
			}

			return arch.UnwindStack (this, memory, null, 0);
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
