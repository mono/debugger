using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoRegisterLocation : MonoTargetLocation
	{
		int register;

		internal MonoRegisterLocation (DebuggerBackend backend, StackFrame frame,
					       bool is_byref, int register, long offset,
					       TargetAddress start_scope, TargetAddress end_scope)
			: base (backend, frame, is_byref, offset, start_scope, end_scope)
		{
			this.register = register;
		}

		public override bool HasAddress {
			get {
				return IsByRef;
			}
		}

		protected override TargetAddress GetAddress ()
		{
			throw new NotImplementedException ();
#if FIXME
			// If this is a reference type, the register just holds the
			// address of the actual data, so read the address from the
			// register and return it.
			long contents = Inferior.GetRegister (register);
			if (contents == 0)
				throw new LocationInvalidException ();

			return new TargetAddress (Inferior, contents);
#endif
		}

		public override ITargetMemoryReader ReadMemory (int size)
		{
			throw new NotImplementedException ();
#if FIXME
			if (IsByRef)
				return base.ReadMemory (size);

			// If this is a valuetype, the register hold the whole data.
			long contents = Inferior.GetRegister (register);

			// We can read at most Inferior.TargetIntegerSize from a register
			// (a word on the target).
			if (Offset + size > Inferior.TargetIntegerSize)
				throw new ArgumentException ();

			// Using ITargetMemoryReader for this is just an ugly hack, but I
			// wanted to hide the fact that the data is cominig from a
			// register from the caller.
			ITargetMemoryReader reader;
			if (Inferior.TargetIntegerSize == 4)
				reader = new TargetReader (BitConverter.GetBytes ((int) contents), Inferior);
			else if (Inferior.TargetIntegerSize == 8)
				reader = new TargetReader (BitConverter.GetBytes (contents), Inferior);
			else
				throw new InternalError ();

			reader.Offset = Offset;
			return reader;
#endif
		}

		protected override MonoTargetLocation Clone (int offset)
		{
			return new MonoRegisterLocation (backend, frame, is_byref, register,
							 Offset + offset, start_scope, end_scope);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0:x}", register);
		}
	}
}
