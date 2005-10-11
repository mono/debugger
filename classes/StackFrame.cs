using System;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger.Languages;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public delegate void ObjectInvalidHandler (object obj);

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
				if (!from_current_frame)
					throw new InvalidOperationException ();

				long[] retval = new long [regs.Length];
				for (int i = 0; i < regs.Length; i++)
					retval [i] = regs [i].Value;

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
	public sealed class StackFrame : MarshalByRefObject, IDisposable
	{
		protected readonly TargetAddress address;
		protected readonly TargetAddress stack_pointer;
		protected readonly TargetAddress frame_address;
		protected readonly Registers registers;
		protected readonly int level;

		Method method;
		Process process;
		TargetAccess target;
		SourceAddress source;
		bool has_source;
		AddressDomain address_domain;
		Symbol name;

		internal StackFrame (Process process, TargetAccess target,
				     TargetAddress address, TargetAddress stack_pointer,
				     TargetAddress frame_address, Registers registers,
				     int level)
		{
			this.process = process;
			this.target = target;
			this.address = address;
			this.stack_pointer = stack_pointer;
			this.frame_address = frame_address;
			this.registers = registers;
			this.level = level;
		}

		internal StackFrame (Process process, TargetAccess target,
				     TargetAddress address, TargetAddress stack_pointer,
				     TargetAddress frame_address, Registers registers,
				     int level, Symbol name)
			: this (process, target, address, stack_pointer, frame_address,
				registers, level)
		{
			this.name = name;
		}

		internal StackFrame (Process process, TargetAccess target,
				     TargetAddress address, TargetAddress stack_pointer,
				     TargetAddress frame_address, Registers registers,
				     int level, Method method)
			: this (process, target, address, stack_pointer, frame_address,
				registers, level)
		{
			this.method = method;
			this.name = new Symbol (method.Name, method.StartAddress, 0);
		}


		internal StackFrame (Process process, TargetAccess target,
				     TargetAddress address, TargetAddress stack_pointer,
				     TargetAddress frame_address, Registers registers,
				     int level, Method method, SourceAddress source)
			: this (process, target, address, stack_pointer, frame_address,
				registers, level, method)
		{
			this.source = source;
			this.has_source = true;
		}

		public int Level {
			get {
				return level;
			}
		}

		public bool IsValid {
			get {
				return !disposed;
			}
		}

		public SourceAddress SourceAddress {
			get {
				check_disposed ();
				if (has_source)
					return source;
				if ((method != null) && method.HasSource)
					source = method.Source.Lookup (address);
				has_source = true;
				return source;
			}
		}

		public TargetAddress TargetAddress {
			get {
				check_disposed ();
				return address;
			}
		}

		public TargetAddress StackPointer {
			get {
				check_disposed ();
				return stack_pointer;
			}
		}

		public TargetAddress FrameAddress {
			get {
				check_disposed ();
				return frame_address;
			}
		}

		public AddressDomain AddressDomain {
			get {
				if (address_domain == null)
					address_domain = new AddressDomain ("frame");

				return address_domain;
			}
		}

		public Process Process {
			get {
				check_disposed ();
				return process;
			}
		}

		public TargetAccess TargetAccess {
			get {
				check_disposed ();
				return target;
			}
		}

		public Registers Registers {
			get {
				check_disposed ();
				return registers;
			}
		}

		public long GetRegister (int index)
		{
			return Registers [index].Value;
		}

		public Method Method {
			get {
				check_disposed ();
				return method;
			}
		}

		public Symbol Name {
			get {
				check_disposed ();
				return name;
			}
		}

		public TargetVariable[] Locals {
			get {
				check_disposed ();
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

		public event ObjectInvalidHandler FrameInvalidEvent;

		public Language Language {
			get {
				check_disposed ();
				if (method != null)
					return method.Module.Language;
				else
					return process.NativeLanguage;
			}
		}

		public StackFrame UnwindStack (ITargetMemoryAccess memory, Architecture arch)
		{
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

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed) {
				Console.WriteLine ("StackFrame already disposed: {0}{1}", this, Environment.StackTrace);
				// throw new ObjectDisposedException ("StackFrame");
			}
		}

		protected void Dispose (bool disposing)
		{
			if (!this.disposed) {
				if (disposing) {
					if (FrameInvalidEvent != null)
						FrameInvalidEvent (this);

					if (address_domain != null)
						address_domain.Dispose ();

					method = null;
					source = null;
				}
				
				this.disposed = true;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~StackFrame ()
		{
			Dispose (false);
		}

	}
}
