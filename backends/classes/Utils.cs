using System;
using System.IO;

namespace Mono.Debugger.Backends
{
	internal abstract class Utils
	{
		public static string GetFileContents (string filename)
		{
			try {
				StreamReader sr = File.OpenText (filename);
				string contents = sr.ReadToEnd ();

				sr.Close();

				return contents;
			}
			catch {
				return null;
			}
		}
		
	}
}
