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
			return (ISourceBuffer) ((ObjectCache) files [name]).Data;

		ObjectCache cache = (ObjectCache) files [name];
		if (cache == null) {
			cache = new ObjectCache (new ObjectCacheFunc (read_file), name, 10);
			files.Add (name, cache);
		}

		return (ISourceBuffer) cache.Data;
	}

	object read_file (object user_data)
	{
		string name = (string) user_data;

		FileInfo file_info = new FileInfo (name);

		if (!file_info.Exists) {
			Console.WriteLine ("Can't find source file: " + name);
			return null;
		}

		ArrayList contents = new ArrayList ();
		try {
			Encoding encoding = Encoding.GetEncoding (28591);
			using (StreamReader reader = new StreamReader (file_info.OpenRead (), encoding)) {
				string line;
				while ((line = reader.ReadLine ()) != null)
					contents.Add (line);
			}
		} catch {
			return null;
		}

		return new SourceBuffer (name, contents);
	}
}
