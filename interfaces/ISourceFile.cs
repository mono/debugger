using System;
using System.IO;

namespace Mono.Debugger
{
	public interface ISourceFileFactory
	{
		ISourceFile FindFile (string name);
	}

	public interface ISourceFile
	{
		FileInfo FileInfo {
			get;
		}

		string FileContents {
			get;
		}
	}
}
