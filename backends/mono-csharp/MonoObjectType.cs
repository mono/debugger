using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoObjectType : MonoType, ITargetPointerType
	{
		internal readonly MonoSymbolTable Table;

		public MonoObjectType (Type type, int size, TargetAddress klass, MonoSymbolTable table)
			: base (TargetObjectKind.Pointer, type, size, klass, true)
		{
			this.Table = table;
		}

		public MonoObjectType (MonoType type, TargetAddress klass, MonoSymbolTable table)
			: this ((Type) type.TypeHandle, type.Size, klass, table)
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

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoObjectObject (this, location);
		}
	}
}
