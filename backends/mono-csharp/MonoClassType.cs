using System;
using System.Reflection;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoClassType : MonoStructType, ITargetClassType
	{
		public readonly MonoClassType ParentType;

		public MonoClassType (Type type, int size, TargetBinaryReader info,
				      MonoSymbolFileTable table)
			: base (TargetObjectKind.Class, type, size, info, table)
		{
			int parent_type_info = info.ReadInt32 ();
			if ((parent_type_info != 0) && (type.BaseType != null)) {
				MonoType parent = GetType (type.BaseType, parent_type_info, table);
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

		public override MonoObject GetObject (MonoTargetLocation location)
		{
			return new MonoClassObject (this, location);
		}
	}
}
