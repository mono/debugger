using System;
using System.Reflection;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoClassType : MonoStructType, ITargetClassType
	{
		public readonly MonoClassType ParentType;

		public MonoClassType (Type type, int size, ITargetMemoryReader info)
			: base (type, size, info)
		{
			TargetAddress parent_type_info = info.ReadAddress ();
			if (parent_type_info.Address != 0) {
				MonoType parent = GetType (
					type.BaseType, info.TargetMemoryAccess, parent_type_info);
				ParentType = parent as MonoClassType;
			}
		}

		public bool HasParent {
			get {
				return ParentType != null;
			}
		}

		ITargetClassType ITargetClassType.ParentType {
			get {
				if (!HasParent)
					throw new InvalidOperationException ();

				return ParentType;
			}
		}

		public override MonoObject GetObject (ITargetLocation location)
		{
			return new MonoClassObject (this, location);
		}
	}
}
