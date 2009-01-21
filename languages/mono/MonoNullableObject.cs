using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoNullableObject : TargetNullableObject
	{
		new MonoNullableType Type;

		public MonoNullableObject (MonoNullableType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		internal override bool HasValue (TargetMemoryAccess target)
		{
			byte[] buffer = Location.ReadBuffer (target, Type.ElementType.Size + 1);
			return buffer [Type.ElementType.Size] != 0;
		}

		internal override TargetObject GetValue (TargetMemoryAccess target)
		{
			return Type.ElementType.GetObject (target, Location);
		}
	}
}
