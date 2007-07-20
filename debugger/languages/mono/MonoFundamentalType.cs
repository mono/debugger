using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFundamentalType : TargetFundamentalType
	{
		MonoSymbolFile file;
		MonoClassType class_type;

		public MonoFundamentalType (MonoSymbolFile file, Cecil.TypeDefinition type,
					    FundamentalKind kind, int size)
			: base (file.Language, type.FullName, kind, size)
		{
			this.file = file;

			class_type = new MonoClassType (file, type);
		}

		public MonoClassType ClassType {
			get { return class_type; }
		}
	}
}
