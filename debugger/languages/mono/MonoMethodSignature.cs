using System;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoMethodSignature : TargetMethodSignature
	{
		readonly TargetType ret_type;
		readonly TargetType[] param_types;

		public MonoMethodSignature (TargetType ret_type, TargetType[] param_types)
		{
			this.ret_type = ret_type;
			this.param_types = param_types;
		}

		public override TargetType ReturnType {
			get { return ret_type; }
		}

		public override TargetType[] ParameterTypes {
			get { return param_types; }
		}
	}
}
