using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public class GnuReadLine
	{
		[DllImport("libmonodebuggerreadline")]
		extern static void mono_debugger_readline_init ();

		[DllImport("libmonodebuggerreadline")]
		extern static int mono_debugger_readline_is_a_tty (int fd);

		[DllImport("libmonodebuggerreadline")]
		extern static string mono_debugger_readline_readline (IntPtr channel, string prompt);

		[DllImport("libmonodebuggerreadline")]
		extern static string mono_debugger_readline_add_history (string line);

		IOChannel channel;
		string prompt;

		public static GnuReadLine ()
		{
			mono_debugger_readline_init ();
		}

		public GnuReadLine (IOChannel channel, string prompt)
		{
			this.channel = channel;
			this.prompt = prompt;
		}

		public static bool IsTerminal (int fd)
		{
			return mono_debugger_readline_is_a_tty (fd) != 0;
		}

		public string ReadLine ()
		{
			return mono_debugger_readline_readline (channel.Channel, prompt);
		}

		public void AddHistory (string line)
		{
			mono_debugger_readline_add_history (line);
		}
	}
}
