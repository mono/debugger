using System;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger.Languages;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public delegate void ObjectInvalidHandler (object obj);

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

		public void WriteRegister (ITargetAccess target, long value)
		{
			this.value = value;

			if (addr_on_stack.IsNull)
				target.SetRegisters (registers);
			else if (Size == target.TargetIntegerSize)
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
	}

	public sealed class Registers
	{
		Register[] regs;
		bool from_current_frame;

		public Registers (IArchitecture arch)
		{
			regs = new Register [arch.CountRegisters];
			for (int i = 0; i < regs.Length; i++)
				regs [i] = new Register (
					this, i, arch.RegisterSizes [i], false, 0);
		}

		public Registers (IArchitecture arch, long[] values)
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

	public sealed class StackFrame : IDisposable
	{
		IMethod method;
		Process process;
		TargetAddress address, stack, frame;
		SourceAddress source;
		AddressDomain address_domain;
		ILanguage language;
		bool has_registers;
		Registers registers;
		string name;
		int level;

		public StackFrame (Process process, TargetAddress address, TargetAddress stack,
				   TargetAddress frame, Registers registers, int level,
				   IMethod method, SourceAddress source)
		{
			this.process = process;
			this.address = address;
			this.stack = stack;
			this.frame = frame;
			this.registers = registers;
			this.level = level;
			this.method = method;
			this.source = source;
			this.name = method != null ? method.Name : null;

			if (method != null)
				language = method.Module.Language;
		}

		internal static StackFrame CreateFrame (Process process,
							Inferior.StackFrame frame,
							Registers regs, int level,
							IMethod method)
		{
			SourceAddress source = null;
			if ((method != null) && method.HasSource)
				source = method.Source.Lookup (frame.Address);
			return CreateFrame (process, frame, regs, level, method, source);
		}

		internal static StackFrame CreateFrame (Process process,
							Inferior.StackFrame frame,
							Registers regs, int level,
							IMethod method, SourceAddress source)
		{
			return new StackFrame (process, frame.Address, frame.StackPointer,
					       frame.FrameAddress, regs, level, method,
					       source);
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
				return stack;
			}
		}

		public TargetAddress FrameAddress {
			get {
				check_disposed ();
				return frame;
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

		public ITargetAccess TargetAccess {
			get {
				check_disposed ();
				return process;
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

		public TargetLocation GetRegisterLocation (int index, long reg_offset,
							   bool dereference, long offset)
		{
			return new MonoVariableLocation (
				this, dereference, index, reg_offset, false, offset);
		}

		public IMethod Method {
			get {
				check_disposed ();
				return method;
			}
		}

		public string Name {
			get {
				check_disposed ();
				return name;
			}
		}

		public IVariable[] Locals {
			get {
				check_disposed ();
				ArrayList list = new ArrayList ();
				foreach (IVariable local in Method.Locals) {
					if (local.IsAlive (TargetAddress))
						list.Add (local);
				}
				IVariable[] retval = new IVariable [list.Count];
				list.CopyTo (retval, 0);
				return retval;
			}
		}

		public void SetRegister (int index, long value)
		{
			registers [index].WriteRegister (process, value);
		}

		public event ObjectInvalidHandler FrameInvalidEvent;

		public ILanguage Language {
			get {
				check_disposed ();
				return language;
			}
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

			if (source != null)
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
				Console.WriteLine ("FUCK: {0}", this);
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
