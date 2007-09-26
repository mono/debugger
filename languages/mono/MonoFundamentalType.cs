using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFundamentalType : TargetFundamentalType
	{
		MonoSymbolFile file;
		MonoClassType class_type;

		public MonoFundamentalType (MonoSymbolFile file, Cecil.TypeDefinition type,
					    string name, FundamentalKind kind, int size)
			: base (file.Language, name, kind, size)
		{
			this.file = file;
			this.class_type = new MonoClassType (file, type);
		}

		public override bool HasClassType {
			get { return true; }
		}

		public override TargetClassType ClassType {
			get { return class_type; }
		}

		internal MonoClassType MonoClassType {
			get { return class_type; }
		}
	}
}
