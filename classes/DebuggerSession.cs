using System;
using System.IO;
using System.Configuration;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using Mono.Debugger.Languages;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	[Serializable]
	public class DebuggerOptions
	{
		/* The executable file we're debugging */
		public string File = null;

		/* argv[1...n] for the inferior process */
		public string[] InferiorArgs = null;

		/* The command line prompt.  should we really even
		 * bother letting the user set this?  why? */
		public string Prompt = "(mdb) ";

		/* JIT optimization flags affecting the inferior
		 * process */
		public string JitOptimizations = "";

		public string[] JitArguments = null;

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

		/* non-null if the user specified the -mono command line argument */
		public string MonoPath = null;

		public string RemoteHost = null;
		public string RemoteMono = null;
	}

	[Serializable]
	public class DebuggerSession
	{
		public readonly DebuggerOptions Options;
		byte[] session_data;

		public DebuggerSession (DebuggerOptions options)
		{
			this.Options = options;
		}

		public void MainProcessExited (Process process)
		{
			using (MemoryStream stream = new MemoryStream ()) {
				process.SaveSession (stream, StreamingContextStates.Persistence);
				session_data = stream.ToArray ();
			}
		}

		public void MainProcessReachedMain (Process process)
		{
			if (session_data == null)
				return;

			using (MemoryStream stream = new MemoryStream (session_data)) {
				process.LoadSession (stream, StreamingContextStates.Persistence);
			}
		}

		//
		// Private stuff.
		//

		internal static ISurrogateSelector CreateSurrogateSelector (StreamingContext context)
		{
			return new SurrogateSelector ();
		}

		private class SurrogateSelector : ISurrogateSelector
		{
			void ISurrogateSelector.ChainSelector (ISurrogateSelector selector)
			{
				throw new NotImplementedException ();
			}

			ISurrogateSelector ISurrogateSelector.GetNextSelector()
			{
				throw new NotImplementedException ();
			}

			ISerializationSurrogate ISurrogateSelector.GetSurrogate (
				Type type, StreamingContext context, out ISurrogateSelector selector)
			{
				if (type == typeof (Module)) {
					selector = this;
					return new Module.SessionSurrogate ();
				}

				if (type == typeof (ThreadGroup)) {
					selector = this;
					return new ThreadGroup.SessionSurrogate ();
				}

				if (type.IsSubclassOf (typeof (Event))) {
					selector = this;
					return new Event.SessionSurrogate ();
				}

				if ((type == typeof (SourceLocation)) ||
				    type.IsSubclassOf (typeof (SourceLocation))) {
					selector = this;
					return new SourceLocation.SessionSurrogate ();
				}

				if (type.IsSubclassOf (typeof (TargetFunctionType))) {
					selector = this;
					return new TargetFunctionType.SessionSurrogate ();
				}

				selector = null;
				return null;
			}
		}
	}
}
