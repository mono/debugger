using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public class GnuReadLine
	{
		[DllImport("libmonodebuggerreadline")]
		extern static void mono_debugger_readline_static_init ();

		[DllImport("libmonodebuggerreadline")]
		extern static int mono_debugger_readline_is_a_tty (int fd);

		[DllImport("libmonodebuggerreadline")]
		extern static string mono_debugger_readline_readline (string prompt);

		[DllImport("libmonodebuggerreadline")]
		extern static string mono_debugger_readline_add_history (string line);

		string prompt;

		static GnuReadLine ()
		{
			mono_debugger_readline_static_init ();
		}

		public GnuReadLine (string prompt)
		{
			this.prompt = prompt;
		}

		public static bool IsTerminal (int fd)
		{
			return mono_debugger_readline_is_a_tty (fd) != 0;
		}

		public string ReadLine ()
		{
			return mono_debugger_readline_readline (prompt);
		}

		public void AddHistory (string line)
		{
			mono_debugger_readline_add_history (line);
		}
	}
}
