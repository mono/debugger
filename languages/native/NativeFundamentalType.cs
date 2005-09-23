using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFundamentalType : TargetType, ITargetFundamentalType
	{
		string name;
		int size;
		FundamentalKind fundamental_kind;

		public NativeFundamentalType (ILanguage language, string name,
					      FundamentalKind kind, int size)
			: base (language, TargetObjectKind.Fundamental)
		{
			this.name = name;
			this.size = size;
			this.fundamental_kind = kind;
		}

		public override string Name {
			get { return name; }
		}

		public override int Size {
			get { return size; }
		}

		public override bool HasFixedSize {
			get { return true; }
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

		public override TargetObject GetObject (TargetLocation location)
		{
			return new NativeFundamentalObject (this, location);
		}
	}
}
