using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoStringType : MonoFundamentalType
	{
		int object_size;

		public MonoStringType (MonoSymbolFile file, Type type, int object_size,
				       int size, TargetAddress klass)
			: base (file, type, size, klass)
		{
			this.object_size = object_size;
		}

		protected override MonoTypeInfo CreateTypeInfo ()
		{
			return new MonoStringTypeInfo (this, object_size, size, klass_address);
		}

		public override bool IsByRef {
			get { return true; }
		}

		new public static bool Supports (Type type)
		{
			return type == typeof (string);
		}

		protected override MonoTypeInfo DoResolve (TargetBinaryReader info)
		{
			throw new InvalidOperationException ();
		}
	}
}
