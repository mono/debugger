using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFundamentalType : NativeType, ITargetFundamentalType
	{
		FundamentalKind fundamental_kind;

		public NativeFundamentalType (string name, FundamentalKind kind, int size)
			: base (name, TargetObjectKind.Fundamental, size)
		{
			this.fundamental_kind = kind;
		}

		public override bool IsByRef {
			get {
				switch (fundamental_kind) {
				case FundamentalKind.Object:
				case FundamentalKind.String:
				case FundamentalKind.IntPtr:
				case FundamentalKind.UIntPtr:
					return true;

				default:
					return false;
				}
			}
		}

		public FundamentalKind FundamentalKind {
			get {
				return fundamental_kind;
			}
		}

		public override NativeObject GetObject (TargetLocation location)
		{
			return new NativeFundamentalObject (this, location);
		}
	}
}
