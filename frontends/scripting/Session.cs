using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace Mono.Debugger.Frontends.Scripting
{
	[Serializable]
	public class Session
	{
		public Module[] Modules;
		public BreakpointHandle[] Breakpoints;

		public void Save (Interpreter interpreter, string filename)
		{
			using (FileStream stream = new FileStream (filename, FileMode.Create)) {
				BinaryFormatter bf = new BinaryFormatter ();
				bf.Serialize (stream, interpreter.ProcessStart);
				bf.Serialize (stream, this);
			}
		}

		public static Session Load (Interpreter interpreter, string filename)
		{
			using (FileStream stream = new FileStream (filename, FileMode.Open)) {
				BinaryFormatter bf = new BinaryFormatter ();
				ProcessStart start = (ProcessStart) bf.Deserialize (stream);
				Process process = interpreter.Run (start);

				bf.Context = new StreamingContext (
					StreamingContextStates.All, process);

				return (Session) bf.Deserialize (stream);
			}
		}
	}
}
