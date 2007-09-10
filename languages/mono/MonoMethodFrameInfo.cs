using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoMethodFrameInfo : TargetMethodFrameInfo
	{
		public readonly Method Method;
		public readonly TargetAddress Declaring;
		public readonly MonoGenericInst ClassInst;
		public readonly MonoGenericInst MethodInst;

		public MonoMethodFrameInfo (Method method, TargetAddress declaring,
					    MonoGenericInst class_inst,
					    MonoGenericInst method_inst)
		{
			this.Method = method;
			this.Declaring = declaring;
			this.ClassInst = class_inst;
			this.MethodInst = method_inst;
		}

		public override string ToString ()
		{
			return String.Format ("MonoMethodFrameInfo ({0}:{1}:{2}:{3})",
					      Method, Declaring, ClassInst, MethodInst);
		}
	}
}
