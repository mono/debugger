using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoObjectTypeInfo : MonoTypeInfo
	{
		new MonoObjectType type;

		public MonoObjectTypeInfo (MonoObjectType type, int size, TargetAddress klass)
			: base (type, size, klass)
		{
			this.type = type;
		}

		public new MonoObjectType Type {
			get { return type; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoObjectObject (this, location);
		}
	}
}
