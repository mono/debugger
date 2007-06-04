using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativePointerObject : TargetPointerObject
	{
		public NativePointerObject (NativePointerType type, TargetLocation location)
			: base (type, location)
		{ }

		public override TargetType GetCurrentType (Thread target)
		{
			if (!Type.HasStaticType)
				throw new InvalidOperationException ();

			return Type.StaticType;
		}

		public override TargetObject GetDereferencedObject (Thread target)
		{
			if (!Type.HasStaticType)
				throw new InvalidOperationException ();

			TargetLocation new_location = Location.GetLocationAtOffset (0);
			return Type.StaticType.GetObject (new_location);
		}

		internal override long GetDynamicSize (Thread target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public override TargetObject GetArrayElement (Thread target, int index)
		{
			if (!Type.IsArray)
				throw new InvalidOperationException ();

			int size = Type.Size;
			TargetLocation new_loc = Location.GetLocationAtOffset (index * size);

			if (Type.StaticType.IsByRef)
				new_loc = new_loc.GetDereferencedLocation (target);

			return Type.StaticType.GetObject (new_loc);
		}

		public override string Print (Thread target)
		{
			if (Type.HasStaticType) {
				TargetFundamentalType ftype;
				NativeTypeAlias alias = Type.StaticType as NativeTypeAlias;
				if (alias != null)
					ftype = alias.TargetType as TargetFundamentalType;
				else
					ftype = Type.StaticType as TargetFundamentalType;

				if ((ftype != null) && (ftype.Name == "char")) {
					TargetObject sobj = Type.Language.StringType.GetObject (Location);
					if (sobj != null)
						return sobj.Print (target);
				}
			}

			if (HasAddress) {
				if (Address.IsNull)
					return "0x0";
				else
					return String.Format ("{0}", Address);
			} else {
				byte[] data = Location.ReadBuffer (target, type.Size);

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

