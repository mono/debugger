using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public class SourceBuffer : ISourceBuffer
	{
		string name;
		string contents;
		bool has_contents;

		public SourceBuffer (string name, string contents)
		{
			this.name = name;
			this.contents = contents;
			this.has_contents = true;
		}

		public SourceBuffer (string name)
		{
			this.name = name;
			this.has_contents = false;
		}

		public string Name {
			get {
				return name;
			}
		}

		public bool HasContents {
			get {
				return has_contents;
			}
		}

		public string Contents {
			get {
				if (!HasContents)
					throw new InvalidOperationException ();

				return contents;
			}
		}
	}
}
