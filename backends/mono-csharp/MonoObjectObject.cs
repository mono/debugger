using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoObjectObject : MonoObject, ITargetPointerObject
	{
		new MonoObjectType type;

		public MonoObjectObject (MonoObjectType type, ITargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public override bool HasObject {
			get {
				return CurrentType.HasObject;
			}
		}

		public new ITargetPointerType Type {
			get {
				return type;
			}
		}

		bool ITargetObject.HasObject {
			get {
				return HasObject;
			}
		}

		public MonoType CurrentType {
			get {
				ITargetMemoryAccess memory;
				TargetAddress address = GetAddress (location, out memory);

				try {
					address = memory.ReadAddress (address);
					address = memory.ReadAddress (address);

					return type.Table.GetTypeFromClass (address.Address);
				} catch {
					throw new LocationInvalidException ();
				}
			}
		}

		ITargetType ITargetPointerObject.CurrentType {
			get {
				return CurrentType;
			}
		}

		public byte[] GetDereferencedContents (int size)
		{
			throw new InvalidOperationException ();
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader, TargetAddress address,
							out TargetAddress dynamic_address)
		{
			throw new InvalidOperationException ();
		}

		protected override object GetObject (ITargetMemoryReader reader, TargetAddress address)
		{
			ITargetLocation new_location = new RelativeTargetLocation (
				location, address + type.Size);

			return CurrentType.GetObject (new_location);
		}
	}
}
