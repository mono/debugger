using System;

namespace Mono.Debugger.Interface
{
	public interface ITargetFundamentalObject : ITargetObject
	{
		new ITargetFundamentalType Type {
			get;
		}

		object GetObject (IThread target);

		void SetObject (IThread target, object obj);
	}
}
