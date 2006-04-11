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
		SourceMethod source;
		int line;

		public Module Module {
			get { return module; }
		}

		public bool HasSourceFile {
			get { return source != null; }
		}

		public SourceFile SourceFile {
			get {
				if (!HasSourceFile)
					throw new InvalidOperationException ();

				return source.SourceFile;
			}
		}

		public SourceMethod Method {
			get {
				if (!HasSourceFile)
					throw new InvalidOperationException ();

				return source;
			}
		}

		public int Line {
			get {
				if (line == -1)
					return source.StartRow;
				else
					return line;
			}
		}

		public string Name {
			get {
				if (line == -1)
					return source.Name;
				else
					return String.Format ("{0}:{1}", SourceFile.FileName, line);
			}
		}

		public SourceLocation (SourceMethod source)
			: this (source, -1)
		{ }

		public SourceLocation (SourceMethod source, int line)
		{
			this.module = source.SourceFile.Module;
			this.source = source;
			this.line = line;

			if (source == null)
				throw new InvalidOperationException ();
		}

		internal TargetAddress GetAddress (int domain)
		{
			Method method = source.GetMethod (domain);
			if (method == null)
				return TargetAddress.Null;

			if (line != -1) {
				if (method.HasSource)
					return method.Source.Lookup (line);
				else
					return TargetAddress.Null;
			} else if (method.HasMethodBounds)
				return method.MethodStartAddress;
			else
				return method.StartAddress;
		}

		public override string ToString ()
		{
			return String.Format ("SourceLocation ({0})", Name);
		}
	}
}
