using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFundamentalType : NativeType, ITargetFundamentalType
	{
		Type type;

		public NativeFundamentalType (string name, Type type, int size)
			: base (name, TargetObjectKind.Fundamental, size)
		{
			this.type = type;
		}

		public override bool IsByRef {
			get {
				return type.IsByRef;
			}
		}

		public Type Type {
			get {
				return type;
			}
		}

		public TypeCode TypeCode {
			get {
				return Type.GetTypeCode (type);
			}
		}

		public override NativeObject GetObject (TargetLocation location)
		{
			return new NativeFundamentalObject (this, location);
		}
	}
}
