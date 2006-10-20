using System;

namespace Mono.Debugger.Interface
{
	public interface IProcessStart
	{
		string[] CommandLineArguments {
			get;
		}

		string[] Environment {
			get;
		}

		string TargetApplication {
			get;
		}

		string WorkingDirectory {
			get;
		}
	}
}
