using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoOpaqueObject : MonoObject, ITargetPointerObject
	{
		new MonoOpaqueType type;

		public MonoOpaqueObject (MonoOpaqueType type, ITargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public new ITargetPointerType Type {
			get {
				return type;
			}
		}

		public override bool HasObject {
			get {
				return false;
			}
		}

		bool ITargetObject.HasObject {
			get {
				return false;
			}
		}

		ITargetType ITargetPointerObject.CurrentType {
			get {
				throw new InvalidOperationException ();
			}
		}

		public byte[] GetDereferencedContents (int size)
		{
			ITargetMemoryAccess memory;
			TargetAddress address = GetAddress (location, out memory);
			return memory.ReadBuffer (address, size);
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader, TargetAddress address,
							out TargetAddress dynamic_address)
		{
			throw new InvalidOperationException ();
		}

		protected override object GetObject (ITargetMemoryReader reader, TargetAddress address)
		{
			throw new InvalidOperationException ();
		}
	}
}

