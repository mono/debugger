using System;
using System.Reflection;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoClassType : MonoStructType, ITargetClassType
	{
		public readonly MonoClassType ParentType;

		public MonoClassType (Type type, int size, TargetAddress klass,
				      TargetBinaryReader info, MonoSymbolTable table)
			: base (TargetObjectKind.Class, type, size, klass, info, table)
		{
			int parent_type_info = info.ReadInt32 ();
			if (type.BaseType != null) {
				if (parent_type_info != 0) {
					MonoType parent = GetType (type.BaseType, parent_type_info, table);
					ParentType = parent as MonoClassType;
				} else
					ParentType = ObjectClass;
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

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoClassObject (this, location);
		}
	}
}
