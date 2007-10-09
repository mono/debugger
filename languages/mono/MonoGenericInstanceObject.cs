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
			return ((MonoFieldInfo) field).DeclaringType.GetField (target, Location, field);
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
