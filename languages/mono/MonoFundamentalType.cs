using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFundamentalType : TargetFundamentalType
	{
		MonoSymbolFile file;

		public MonoFundamentalType (MonoSymbolFile file, Cecil.TypeDefinition type,
					    string name, FundamentalKind kind, int size)
			: base (file.Language, name, kind, new MonoClassType (file, type), size)
		{
			this.file = file;
		}

		new public MonoClassType ClassType {
			get { return (MonoClassType) base.ClassType; }
		}
	}
}
