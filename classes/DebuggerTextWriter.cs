using System;
using System.Text;

namespace Mono.Debugger
{
	public abstract class DebuggerTextWriter
	{
		public abstract void Write (bool is_stderr, string text);

		public virtual void WriteLine (bool is_stderr, string line)
		{
			Write (is_stderr, line);
			Write (is_stderr, "\n");
		}
	}

	public class ConsoleTextWriter : DebuggerTextWriter
	{
		public override void Write (bool is_stderr, string text)
		{
			Console.Write (text);
		}
	}
}
