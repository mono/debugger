using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public class SourceBuffer : ISourceBuffer
	{
		string name;
		string contents;

		public SourceBuffer (string name, string contents)
		{
			this.name = name;
			this.contents = contents;
		}

		public string Name {
			get {
				return name;
			}
		}

		public string Contents {
			get {
				return contents;
			}
		}
	}
}
