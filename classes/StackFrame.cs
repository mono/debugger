using System;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public delegate void ObjectInvalidHandler (object obj);

	public struct Register
	{
		public readonly int Index;
		public readonly object Data;

		public Register (int index, object data)
		{
			this.Index = index;
			this.Data = data;
		}
	}

	public abstract class StackFrame : IDisposable
	{
		IMethod method;
		TargetAddress address;
		SourceAddress source;
		AddressDomain address_domain;
		string name;
		int level;

		public StackFrame (TargetAddress address, int level,
				   SourceAddress source, IMethod method)
			: this (address, level, method.Name)
		{
			this.source = source;
			this.method = method;
		}

		public StackFrame (TargetAddress address, int level, string name)
		{
			this.address = address;
			this.level = level;
			this.name = name;
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

		public virtual AddressDomain AddressDomain {
			get {
				if (address_domain == null)
					address_domain = new AddressDomain ("frame");

				return address_domain;
			}
		}

		public abstract ITargetAccess TargetAccess {
			get;
		}

		public abstract Register[] Registers {
			get;
		}

		public virtual long GetRegister (int index)
		{
			foreach (Register register in Registers) {
				if (register.Index == index)
					return (long) register.Data;
			}

			throw new NoSuchRegisterException ();
		}

		public abstract TargetLocation GetRegisterLocation (int index, long reg_offset, bool dereference, long offset);

		public abstract void SetRegister (int index, long value);

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

		public event ObjectInvalidHandler FrameInvalidEvent;

		public AssemblerLine DisassembleInstruction (TargetAddress address)
		{
			check_disposed ();

			if ((method == null) || !method.IsLoaded)
				return DoDisassembleInstruction (address);

			if ((address < method.StartAddress) || (address >= method.EndAddress))
				throw new ArgumentException ();

			return DoDisassembleInstruction (address);
		}

		protected abstract AssemblerLine DoDisassembleInstruction (TargetAddress address);

		public abstract AssemblerMethod DisassembleMethod ();

		public abstract bool RuntimeInvoke (TargetAddress method_argument,
						    TargetAddress object_argument,
						    TargetAddress[] param_objects);

		public abstract TargetAddress RuntimeInvoke (TargetAddress method_argument,
							     TargetAddress object_argument,
							     TargetAddress[] param_objects,
							     out TargetAddress exc_object);

		public abstract ILanguage Language {
			get;
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
			if (disposed)
				throw new ObjectDisposedException ("StackFrame");
		}

		protected virtual void Dispose (bool disposing)
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
