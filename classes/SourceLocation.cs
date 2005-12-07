using System;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	// <summary>
	//   Represents a location in the source code on which we can insert a breakpoint.
	//   Instances of this class are normally created as the result of a user action
	//   such as a method lookup.
	// </summary>
	[Serializable]
	public class SourceLocation
	{
		Module module;
		SourceMethod method;
		ISourceBuffer buffer;
		int line;

		public Module Module {
			get { return module; }
		}

		public bool HasSourceFile {
			get { return method != null; }
		}

		public ISourceBuffer SourceBuffer {
			get {
				if (HasSourceFile)
					throw new InvalidOperationException ();

				return buffer;
			}
		}

		public SourceFile SourceFile {
			get {
				if (!HasSourceFile)
					throw new InvalidOperationException ();

				return method.SourceFile;
			}
		}

		public SourceMethod Method {
			get {
				if (!HasSourceFile)
					throw new InvalidOperationException ();

				return method;
			}
		}

		public int Line {
			get {
				if (line == -1)
					return method.StartRow;
				else
					return line;
			}
		}

		public string Name {
			get {
				if (line == -1)
					return method.Name;
				else if (HasSourceFile)
					return String.Format ("{0}:{1}", SourceFile.FileName, line);
				else
					return String.Format ("{0}:{1}", SourceBuffer.Name, line);
			}
		}

		public SourceLocation (SourceMethod method)
			: this (method, -1)
		{ }

		public SourceLocation (SourceMethod method, int line)
		{
			this.module = method.SourceFile.Module;
			this.method = method;
			this.line = line;

			if (method == null)
				throw new InvalidOperationException ();
		}

		public SourceLocation (Module module, ISourceBuffer buffer, int line)
		{
			this.module = module;
			this.buffer = buffer;
			this.line = line;
		}

		internal TargetAddress GetAddress ()
		{
			if (!method.IsLoaded)
				throw new InvalidOperationException ();

			if (line != -1)
				return method.Lookup (line);
			else if (method.Method.HasMethodBounds)
				return method.Method.MethodStartAddress;
			else
				return method.Method.StartAddress;
		}

		public override string ToString ()
		{
			return String.Format ("SourceLocation ({0})", Name);
		}
	}
}
