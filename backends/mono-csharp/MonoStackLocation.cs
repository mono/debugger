using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStackLocation : MonoTargetLocation
	{
		bool is_local;
		long stack_offset;

		// <remarks>
		//   @stack_offset is where this variable is located on the stack; @offset
		//   will become the `Offset' property.
		//
		//   It is important that we distinguish these two offsets here to make
		//   things work for reference types: you do not want to modify the
		//   variable's location on the stack, but use an offset in its actual
		//   contents.
		//
		//   For valuetypes, these two are actually the same.
		// </remarks>
		internal MonoStackLocation (DebuggerBackend backend, StackFrame frame,
					    bool is_byref, bool is_local, long stack_offset,
					    long offset, TargetAddress start_scope, TargetAddress end_scope)
			: base (backend, frame, is_byref, offset, start_scope, end_scope)
		{
			this.is_local = is_local;
			this.stack_offset = stack_offset;
		}

		public override bool HasAddress {
			get {
				return true;
			}
		}

		protected override TargetAddress GetAddress ()
		{
			// First get the address of this variable on the stack.
			TargetAddress base_address = is_local ? iframe.LocalsAddress : iframe.ParamsAddress;
			TargetAddress address;
			if (is_local)
				address = new TargetAddress (frame, base_address.Address + stack_offset);
			else
				address = new TargetAddress (frame, base_address.Address + stack_offset);

			// If this is a reference type, there's just a pointer to the
			// actual contents on the stack which we need to dereference.
			if (IsByRef)
				return Inferior.ReadAddress (address);
			else
				return address;
		}

		protected override MonoTargetLocation Clone (int offset)
		{
			return new MonoStackLocation (backend, frame, is_byref, is_local,
						      stack_offset, Offset + offset,
						      start_scope, end_scope);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}:{1}", is_local, stack_offset);
		}
	}
}
