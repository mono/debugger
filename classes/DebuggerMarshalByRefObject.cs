using System;

namespace Mono.Debugger
{
	public class DebuggerMarshalByRefObject : MarshalByRefObject
	{
		public override object InitializeLifetimeService()
		{
			return null;
		}
	}
}
