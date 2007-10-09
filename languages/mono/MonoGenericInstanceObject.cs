using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInstanceObject : TargetGenericInstanceObject
	{
		new public MonoGenericInstanceType Type;
		MonoClassInfo class_info;

		public MonoGenericInstanceObject (MonoGenericInstanceType type,
						  MonoClassInfo class_info,
						  TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
			this.class_info = class_info;
		}

		public override TargetObject GetField (TargetMemoryAccess target, TargetFieldInfo field)
		{
			Console.WriteLine ("GENERIC INST GET FIELD: {0} {1}", this, field);

			int offset = class_info.GetFieldOffsets (target) [field.Position];
			TargetType type = class_info.GetFieldTypes (target) [field.Position];

			Console.WriteLine ("GET FIELD: {0} {1} {2} {3}", this, field, type, offset);
			if (!Type.IsByRef)
				offset -= 2 * target.TargetInfo.TargetAddressSize;
			TargetLocation field_loc = Location.GetLocationAtOffset (offset);

			if (type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			if (field_loc.HasAddress && field_loc.GetAddress (target).IsNull)
				return null;

			return type.GetObject (target, field_loc);
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
