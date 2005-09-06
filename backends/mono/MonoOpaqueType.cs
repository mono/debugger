using System;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoOpaqueType : MonoType
	{
		public MonoOpaqueType (MonoSymbolFile file, Cecil.ITypeReference typeref)
			: base (file, TargetObjectKind.Opaque, typeref)
		{ }

		public override bool IsByRef {
			get { return false; }
		}

		protected override MonoTypeInfo DoGetTypeInfo (TargetBinaryReader info)
		{
			return null;
		}
	}
}
