using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFundamentalType : TargetFundamentalType
	{
		public NativeFundamentalType (Language language, string name,
					      FundamentalKind kind, int size)
			: base (language, name, kind, size)
		{ }

		public override bool HasClassType {
			get { return false; }
		}

		public override TargetClassType ClassType {
			get { throw new InvalidOperationException (); }
		}
	}
}
