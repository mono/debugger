using System;
using System.Reflection;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoClassType : MonoStructType, ITargetClassType
	{
		MonoClassType parent;

		public MonoClassType (Type type, int size, TargetAddress klass,
				      TargetBinaryReader info, MonoSymbolTable table)
			: base (TargetObjectKind.Class, type, size, klass, info, table, true)
		{
			if (Klass.HasParent)
				parent = new MonoClassType (Klass.Parent);
		}

		protected MonoClassType (MonoClass klass)
			: base (TargetObjectKind.Class, klass)
		{ }

		bool ITargetClassType.HasParent {
			get {
				return parent != null;
			}
		}

		public MonoClassType ParentType {
			get {
				if (parent == null)
					throw new InvalidOperationException ();

				return parent;
			}
		}

		ITargetClassType ITargetClassType.ParentType {
			get {
				return ParentType;
			}
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoClassObject (this, location);
		}
	}
}
