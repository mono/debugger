using System;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoOpaqueType : MonoType
	{
		Cecil.ITypeReference typeref;

		public MonoOpaqueType (MonoSymbolFile file, Cecil.ITypeReference typeref)
			: base (file, TargetObjectKind.Opaque)
		{
			this.typeref = typeref;
		}

		public Cecil.ITypeReference Type {
			get { return typeref; }
		}

		public override string Name {
			get { return typeref.FullName; }
		}

		public override bool IsByRef {
			get { return false; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		protected override IMonoTypeInfo DoGetTypeInfo ()
		{
			return null;
		}
	}
}
