using System;

namespace Mono.Debugger.Test.Framework
{
	[AttributeUsage(AttributeTargets.Class, AllowMultiple=false)]
	public sealed class DebuggerTestFixtureAttribute : Attribute
	{
		public int Timeout {
			get; set;
		}

		public int Repeat {
			get; set;
		}
	}
}
