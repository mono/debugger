using System;
using System.Diagnostics;

namespace Mono.Debugger.Languages
{
	public abstract class TargetClassType : TargetStructType
	{
		protected TargetClassType (Language language, TargetObjectKind kind)
			: base (language, kind)
		{ }
	}
}
