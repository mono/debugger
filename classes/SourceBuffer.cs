using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public class SourceBuffer : ISourceBuffer
	{
		string name;
		string[] contents;

		public SourceBuffer (string name, string[] contents)
		{
			this.name = name;
			this.contents = contents;
		}

		public SourceBuffer (string name, ICollection contents)
		{
			this.name = name;
			this.contents = new string [contents.Count];
			contents.CopyTo (this.contents, 0);
		}

		public string Name {
			get { return name; }
		}

		public string[] Contents {
			get { return contents; }
		}
	}
}
