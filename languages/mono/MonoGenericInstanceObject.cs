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

		public override TargetObject GetParentObject (Thread target)
		{
			MonoClassInfo parent_info = (MonoClassInfo) class_info.GetParent (target);
			Console.WriteLine ("GET PARENT OBJECT: {0} {1} {2}",
					   this, class_info, parent_info);
			if (parent_info == null)
				return null;

			MonoClassType parent_type = parent_info.MonoClassType;
			Console.WriteLine ("GET PARENT OBJECT #1: {0} {1} {2}", Type, parent_type,
					   parent_info.GenericClass);

			TargetAddress generic_class = parent_info.GenericClass;
			if (!generic_class.IsNull)
				return new MonoClassObject (parent_type, parent_info, Location);

			MonoGenericInstanceType ginst = new MonoGenericInstanceType (
				parent_type, null, parent_info);

			return new MonoGenericInstanceObject (ginst, parent_info, Location);
		}

		public override TargetObject GetField (TargetMemoryAccess target, TargetFieldInfo field)
		{
			return class_info.GetField (target, this, field);
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
