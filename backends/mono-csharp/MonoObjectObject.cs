using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoObjectObject : MonoObject, ITargetPointerObject
	{
		new MonoObjectType type;

		public MonoObjectObject (MonoObjectType type, ITargetLocation location, bool isbyref)
			: base (type, location, isbyref)
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
			MonoType current_type = CurrentType;
			int offset = current_type.IsByRef ? 0 : type.Size;

			ITargetLocation new_location = new RelativeTargetLocation (
				location, address + offset);

			return current_type.GetObject (new_location, false);
		}
	}
}
