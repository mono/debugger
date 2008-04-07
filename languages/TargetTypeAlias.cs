using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetTypeAlias : TargetType
	{
		string name;
		string target_name;

		public TargetTypeAlias (Language language, string name, string target_name)
			: base (language, TargetObjectKind.Alias)
		{
			this.name = name;
			this.target_name = target_name;
		}

		public override string Name {
			get { return name; }
		}

		public string TargetName {
			get { return target_name; }
		}

		public abstract TargetType TargetType {
			get;
		}
	}
}
