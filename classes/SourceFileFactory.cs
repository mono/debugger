using System;
using System.IO;
using System.Text;
using System.Collections;
using Mono.Debugger;

public class SourceFile : ISourceFile
{
	FileInfo file_info;
	string contents;

	public SourceFile (FileInfo file_info)
	{
		this.file_info = file_info;
		ReadFile ();
	}

	void ReadFile ()
	{
		try {
			FileStream stream = file_info.OpenRead ();

			char[] result = new char [stream.Length];

			long count = 0;
			while (true) {
				int b = stream.ReadByte ();
				if (b == -1)
					break;

				result [count++] = (char) b;
			};

			stream.Close ();

			contents = new String (result);
		} catch {
			return;
		}
	}

	public string Name {
		get {
			return file_info.Name;
		}
	}

	public FileInfo FileInfo {
		get {
			return file_info;
		}
	}

	public string Contents {
		get {
			return contents;
		}
	}
}

public class SourceFileFactory : ISourceFileFactory
{
	Hashtable files = new Hashtable ();

	public ISourceFile FindFile (string name)
	{
		if (files.Contains (name))
			return (ISourceFile) files [name];

		FileInfo file_info = new FileInfo (name);

		if (!file_info.Exists)
			return null;

		ISourceFile retval = new SourceFile (file_info);
		files.Add (name, retval);
		return retval;
	}
}
