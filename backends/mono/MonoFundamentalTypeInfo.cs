using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFundamentalTypeInfo : MonoTypeInfo
	{
		public MonoFundamentalTypeInfo (MonoFundamentalType type, int size, TargetAddress klass)
			: base (type, size, klass)
		{ }

		public override bool HasFixedSize {
			get { return true; }
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoFundamentalObject (this, location);
		}
	}
}
