using System;
using System.Text;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public delegate void StackFrameHandler (StackFrame frame);
	public delegate void StackFrameInvalidHandler ();

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
		SourceLocation source;
		AddressDomain address_domain;
		int level;

		public StackFrame (TargetAddress address, int level,
				   SourceLocation source, IMethod method)
			: this (address, level)
		{
			this.source = source;
			this.method = method;
		}

		public StackFrame (TargetAddress address, int level)
		{
			this.address = address;
			this.level = level;
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

		public SourceLocation SourceLocation {
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

		public abstract ITargetMemoryAccess TargetMemoryAccess {
			get;
		}

		public abstract TargetAddress LocalsAddress {
			get;
		}

		public abstract TargetAddress ParamsAddress {
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

		public IMethod Method {
			get {
				check_disposed ();
				return method;
			}
		}

		public event StackFrameInvalidHandler FrameInvalid;

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
			} else
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
					if (FrameInvalid != null)
						FrameInvalid ();

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
