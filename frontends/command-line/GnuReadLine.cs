using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Frontends.CommandLine
{
	internal class GnuReadLine
	{
		[DllImport("libmonodebuggerreadline")]
		extern static bool mono_debugger_readline_static_init ();

		[DllImport("libmonodebuggerreadline")]
		extern static int mono_debugger_readline_is_a_tty (int fd);

		[DllImport("libmonodebuggerreadline")]
		extern static string mono_debugger_readline_readline (string prompt);

		[DllImport("libmonodebuggerreadline")]
		extern static string mono_debugger_readline_add_history (string line);

		string prompt;
		static bool has_readline;

		static GnuReadLine ()
		{
			has_readline = mono_debugger_readline_static_init ();
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
			if (has_readline)
				return mono_debugger_readline_readline (prompt);
			else {
				Console.Write (prompt);
				return Console.ReadLine ();
			}
		}

		public void AddHistory (string line)
		{
			mono_debugger_readline_add_history (line);
		}
	}
}
