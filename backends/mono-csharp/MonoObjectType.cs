using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoObjectType : MonoType, ITargetPointerType
	{
		internal readonly MonoSymbolFileTable Table;

		public MonoObjectType (Type type, int size, ITargetMemoryReader info,
				       MonoSymbolFileTable table)
			: base (type, size, true)
		{
			this.Table = table;
		}

		public override bool IsByRef {
			get {
				return true;
			}
		}

		public override bool HasObject {
			get {
				return true;
			}
		}

		bool ITargetType.HasObject {
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

		public override MonoObject GetObject (ITargetLocation location)
		{
			return new MonoObjectObject (this, location);
		}
	}
}
