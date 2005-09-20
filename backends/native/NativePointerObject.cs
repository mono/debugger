using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativePointerObject : NativeObject, ITargetPointerObject
	{
		new NativePointerType type;

		public NativePointerObject (NativePointerType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public new ITargetPointerType Type {
			get {
				return type;
			}
		}

		ITargetType ITargetPointerObject.CurrentType {
			get {
				if (!type.HasStaticType)
					throw new InvalidOperationException ();

				return type.StaticType;
			}
		}

		ITargetObject ITargetPointerObject.DereferencedObject {
			get {
				if (!type.HasStaticType)
					throw new InvalidOperationException ();

				TargetLocation new_location = location.GetLocationAtOffset (0, false);
				return type.StaticType.GetObject (new_location);
			}
		}

		public byte[] GetDereferencedContents (int size)
		{
			try {
				return location.ReadBuffer (size);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public bool HasAddress {
			get {
				return location.HasAddress;
			}
		}

		public TargetAddress Address {
			get {
				return location.Address;
			}
		}

		public ITargetObject GetArrayElement (int index)
		{
			if (!type.IsArray)
				throw new InvalidOperationException ();

			int size = type.Size;
			TargetLocation new_location = location.GetLocationAtOffset (
				index * size, type.StaticType.IsByRef);

			return type.StaticType.GetObject (new_location);
		}

		public override string Print ()
		{
			if (HasAddress) {
				if (Address.IsNull)
					return "0x0";
				else
					return String.Format ("{0}", Address);
			} else {
				byte[] data = GetDereferencedContents (type.Size);

				long address;
				if (type.Size == 4)
					address = (uint) BitConverter.ToInt32 (data, 0);
				else
					address = BitConverter.ToInt64 (data, 0);

				return String.Format ("0x{0:x}", address);
			}
		}
	}
}

