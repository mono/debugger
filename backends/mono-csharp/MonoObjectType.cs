using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoObjectType : MonoClass, ITargetPointerType
	{
		public MonoObjectType (Type type, int size, TargetBinaryReader info, MonoSymbolFile table)
			: base (TargetObjectKind.Pointer, type, size, false, info, table, true)
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

		public bool IsArray {
			get {
				return false;
			}
		}

		public ITargetType StaticType {
			get {
				throw new InvalidOperationException ();
			}
		}

		public override MonoClassObject GetClassObject (TargetLocation location)
		{
			return new MonoObjectObject (this, location);
		}

		public MonoObjectObject CreateObject (StackFrame frame, TargetAddress address)
		{
			TargetLocation location = new AbsoluteTargetLocation (frame, address);
			return new MonoObjectObject (this, location);
		}
	}
}
