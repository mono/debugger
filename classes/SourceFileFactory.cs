using System;
using System.IO;
using System.Text;
using System.Collections;
using Mono.Debugger;
using Mono.CSharp.Debugger;

public class SourceFileFactory
{
	Hashtable files = new Hashtable ();

	public ISourceBuffer FindFile (string name)
	{
		if (files.Contains (name))
			return (ISourceBuffer) files [name];

		FileInfo file_info = new FileInfo (name);

		if (!file_info.Exists) {
			Console.WriteLine ("Can't find source file: " + name);
			return null;
		}

		string contents = null;
		try {
			Encoding encoding = Encoding.GetEncoding (28591);
			using (StreamReader reader = new StreamReader (file_info.OpenRead (), encoding)) {
				contents = reader.ReadToEnd ();
			}
		} catch {
			return null;
		}

		SourceBuffer retval = new SourceBuffer (name, contents);
		files.Add (name, retval);
		return retval;
	}
}
