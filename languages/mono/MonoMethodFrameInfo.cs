using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoMethodFrameInfo : TargetMethodFrameInfo
	{
		public readonly Method Method;
		public readonly TargetAddress Declaring;
		public readonly MonoGenericContext Context;

		public MonoMethodFrameInfo (Method method, TargetAddress declaring,
					    MonoGenericContext context)
		{
			this.Method = method;
			this.Declaring = declaring;
			this.Context = context;
		}

		public override string ToString ()
		{
			return String.Format ("MonoMethodFrameInfo ({0}:{1}:{2})",
					      Method, Declaring, Context);
		}
	}
}
