using System;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	// <summary>
	//   Represents a location in the source code on which we can insert a breakpoint.
	//   Instances of this class are normally created as the result of a user action
	//   such as a method lookup.  They can be serialized to disk to persist across
	//   multiple invocations of the same target.
	// </summary>
	public class SourceLocation
	{
		SourceMethod method;
		int line;

		public Module Module {
			get { return method.SourceFile.Module; }
		}

		public SourceFile SourceFile {
			get { return method.SourceFile; }
		}

		public SourceMethod Method {
			get { return method; }
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
				else
					return String.Format ("{0}:{1}", method.Name, line);
			}
		}

		public SourceLocation (SourceMethod method)
			: this (method, -1)
		{ }

		public SourceLocation (SourceMethod method, int line)
		{
			this.method = method;
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

		public BreakpointHandle InsertBreakpoint (Process process, Breakpoint bpt)
		{
			return new BreakpointHandle (process, bpt, this);
		}

		public override string ToString ()
		{
			return String.Format ("SourceLocation ({0})", Name);
		}
	}
}
