using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoType : TargetType
	{
		Type type;
		int size;

		public MonoType (Type type, int size)
		{
			this.type = type;
			this.size = size;
		}

		public override object TypeHandle {
			get {
				return type;
			}
		}

		public override int Size {
			get {
				return size;
			}
		}
	}
}
