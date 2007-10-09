using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetEnumType : TargetType
	{
		public TargetEnumType (Language language)
			: base (language, TargetObjectKind.Enum)
		{ }

		public abstract bool IsFlagsEnum {
			get;
		}

		public abstract TargetEnumInfo Value {
			get;
		}

		public abstract TargetEnumInfo[] Members {
			get;
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target, TargetLocation location)
		{
			return new TargetEnumObject (this, location);
		}
	}
}
