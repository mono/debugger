using System;
using System.IO;

namespace Mono.Debugger
{
	// <summary>
	//   This interface maintains a list of all source files of
	//   the application we're currently debugging.  A debugger
	//   doesn't always know the full pathname of a source file,
	//   but just a relative file name and a list of search
	//   paths.
	// </summary>
	public interface ISourceFileFactory
	{
		// <summary>
		//   Find source file @name.  Unless this is a full
		//   pathname, it'll be searched in the search path.
		// </summary>
		ISourceFile FindFile (string name);
	}

	// <summary>
	//   This is a source buffer which belongs to an actual source
	//   file on disk.
	// </summary>
	public interface ISourceFile : ISourceBuffer
	{
		FileInfo FileInfo {
			get;
		}
	}
}
