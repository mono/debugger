using System;

namespace Mono.Debugger.Interface
{
	public interface ISourceBuffer
	{
		string Name {
			get;
		}

		string[] Contents {
			get;
		}
	}

	public interface ISourceFile
	{
		string Name {
			get;
		}

		string FileName {
			get;
		}
	}
}
