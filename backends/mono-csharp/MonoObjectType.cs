using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoObjectType : MonoType, ITargetPointerType
	{
		internal readonly MonoSymbolFileTable Table;

		public MonoObjectType (Type type, int size, MonoSymbolFileTable table)
			: base (type, size, true)
		{
			this.Table = table;
		}

		public MonoObjectType (MonoType type, MonoSymbolFileTable table)
			: this ((Type) type.TypeHandle, type.Size, table)
		{ }

		public override bool IsByRef {
			get {
				return true;
			}
		}

		public bool IsTypesafe {
			get {
				return true;
			}
		}

		public bool HasStaticType {
			get {
				return false;
			}
		}

		public ITargetType StaticType {
			get {
				throw new InvalidOperationException ();
			}
		}

		public override MonoObject GetObject (MonoTargetLocation location)
		{
			return new MonoObjectObject (this, location);
		}
	}
}
