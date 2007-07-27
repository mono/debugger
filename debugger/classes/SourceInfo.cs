using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	// <summary>
	//   A single source file.  It is used to find a breakpoint's location by method
	//   name or source file.
	// </summary>
	[Serializable]
	public class SourceFile
	{
		public string Name {
			get {
				return Path.GetFileName (filename);
			}
		}

		// <summary>
		//   The file's full pathname.
		// </summary>
		public string FileName {
			get {
				return Path.GetFullPath (filename);
			}
		}

		public Module Module {
			get {
				return module;
			}
		}

		public int ID {
			get {
				return id;
			}
		}

		// <summary>
		//   Returns a list of SourceMethod's which is sorted by source lines.
		//   It is used when inserting a breakpoint by source line to find the
		//   method this line is contained in.
		// </summary>
		public MethodSource[] Methods {
			get {
				return module.GetMethods (this);
			}
		}

		public MethodSource FindMethod (int line)
		{
			MethodSource[] methods = module.GetMethods (this);
			foreach (MethodSource method in methods) {
				if (!method.HasSourceFile)
					continue;
				if ((method.StartRow <= line) && (method.EndRow >= line))
					return method;
			}

			return null;
		}

		public SourceLocation FindLine (int line)
		{
			MethodSource[] methods = module.GetMethods (this);
			foreach (MethodSource method in methods) {
				if (!method.HasSourceFile)
					continue;
				if ((method.StartRow <= line) && (method.EndRow >= line))
					return new SourceLocation (method, line);
			}

			return new SourceLocation (this, line);
		}

		public SourceFile (Module module, string filename)
		{
			this.id = ++next_id;
			this.module = module;
			this.filename = filename;
		}

		public override int GetHashCode ()
		{
			return id;
		}

		public override bool Equals (object o)
		{
			SourceFile file = o as SourceFile;
			if (file == null)
				return false;

			return (id == file.id) && (filename == file.filename) && (module == file.module);
		}

		string filename;
		Module module;
		int id;
		static int next_id = 0;

		public override string ToString ()
		{
			return String.Format ("SourceFile ({0}:{1})", ID, FileName);
		}
	}
}
