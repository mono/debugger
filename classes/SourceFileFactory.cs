using System;
using System.IO;
using System.Text;
using System.Collections;
using Mono.Debugger;
using Mono.CSharp.Debugger;

public class SourceFile : ISourceBuffer
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
			using (StreamReader reader = file_info.OpenText ()) {
				contents = reader.ReadToEnd ();
			}
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

	public bool HasContents {
		get {
			return true;
		}
	}

	public string Contents {
		get {
			return contents;
		}
	}
}

public class SourceFileFactory
{
	Hashtable files = new Hashtable ();

	public SourceFile FindFile (string name)
	{
		if (files.Contains (name))
			return (SourceFile) files [name];

		FileInfo file_info = new FileInfo (name);

		if (!file_info.Exists) {
			Console.WriteLine ("Can't find source file: " + name);
			return null;
		}

		SourceFile retval = new SourceFile (file_info);
		files.Add (name, retval);
		return retval;
	}
}
