using System;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	[Serializable]
	public class SourceLocation : ISerializable
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
			get { return line; }
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

		public int InsertBreakpoint (Breakpoint breakpoint, ThreadGroup group)
		{
			return Module.AddBreakpoint (breakpoint, group, this);
		}

		public void RemoveBreakpoint (int index)
		{
			Module.RemoveBreakpoint (index);
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

		//
		// ISerializable
		//

		public virtual void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			info.AddValue ("method", method);
			info.AddValue ("line", line);
		}

		protected SourceLocation (SerializationInfo info, StreamingContext context)
		{
			method = (SourceMethod) info.GetValue ("method", typeof (SourceMethod));
			line = info.GetInt32 ("line");
		}
	}
}
