using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFundamentalType : NativeType, ITargetFundamentalType
	{
		Type type;

		public NativeFundamentalType (string name, Type type, int size)
			: base (name, TargetObjectKind.Fundamental, size)
		{
			this.type = type;
			this.type_handle = type;
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

		public override NativeObject GetObject (MonoTargetLocation location)
		{
			return new NativeFundamentalObject (this, location);
		}
	}
}
