using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassType : MonoClass
	{
		public MonoClassType (MonoSymbolFile file, Type type)
			: base (file, TargetObjectKind.Class, type)
		{ }

		public override bool IsByRef {
			get { return !Type.IsValueType; }
		}
	}
}
