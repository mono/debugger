using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoVariable : Variable
	{
		public MonoVariable (string name, MonoType type)
			: base (name, type)
		{ }

		public override ITargetLocation Location {
			get {
				throw new NotSupportedException ();
			}
		}
	}
}
