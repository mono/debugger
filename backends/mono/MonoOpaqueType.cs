using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoOpaqueType : MonoType
	{
		public MonoOpaqueType (MonoSymbolFile file, Type type)
			: base (file, TargetObjectKind.Opaque, type)
		{ }

		public override bool IsByRef {
			get { return false; }
		}

		protected override MonoTypeInfo DoResolve (TargetBinaryReader info)
		{
			return null;
		}
	}
}
