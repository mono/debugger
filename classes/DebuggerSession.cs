using System;
using System.IO;
using System.Configuration;
using System.Collections;
using System.Collections.Specialized;

namespace Mono.Debugger
{
	[Serializable]
	public class DebuggerOptions
	{
		/* The executable file we're debugging */
		public string File = "";

		/* argv[1...n] for the inferior process */
		public string[] InferiorArgs = null;

		/* The command line prompt.  should we really even
		 * bother letting the user set this?  why? */
		public string Prompt = "(mdb) ";

		/* JIT optimization flags affecting the inferior
		 * process */
		public string JitOptimizations = "";

		/* The inferior process's working directory */
		public string WorkingDirectory = Environment.CurrentDirectory;

		/* Whether or not we load native symbol tables */
		public bool LoadNativeSymbolTable = true;

		/* true if we're running in a script */
		public bool IsScript = false;

		/* true if we want to start the application immediately */
		public bool StartTarget = false;
	  
		/* the value of the -debug-flags: command line argument */
		public bool HasDebugFlags = false;
		public DebugFlags DebugFlags = DebugFlags.None;
		public string DebugOutput = null;

		/* true if -f/-fullname is specified on the command line */
		public bool InEmacs = false;

		/* non-null if the user specified the -mono-prefix
		 * command line argument */
		public string MonoPrefix = null;

		public string RemoteHost = null;
		public string RemoteMono = null;
	}
}
