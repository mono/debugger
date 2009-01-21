using System;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoNullableType : TargetNullableType
	{
		public MonoNullableType (TargetType element_type)
			: base (element_type)
		{ }

		internal override void SetObject (TargetMemoryAccess target, TargetLocation location,
						  TargetObject obj)
		{
			TargetLocation flag_loc = location.GetLocationAtOffset (ElementType.Size);
			byte[] buffer = new byte [1];

			if (obj is MonoNullObject) {
				buffer [0] = 0;
				flag_loc.WriteBuffer (target, buffer);
				return;
			}

			MonoNullableObject nobj = obj as MonoNullableObject;
			if (nobj != null) {
				if (!nobj.HasValue (target)) {
					buffer [0] = 0;
					flag_loc.WriteBuffer (target, buffer);
					return;
				} else {
					obj = nobj.GetValue (target);
				}
			}

			buffer [0] = 1;
			flag_loc.WriteBuffer (target, buffer);

			ElementType.SetObject (target, location, obj);
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target, TargetLocation location)
		{
			return new MonoNullableObject (this, location);
		}
	}
}
