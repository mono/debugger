using System;
using System.Runtime.InteropServices;
using GLib;

namespace Mono.Debugger
{
	public class GnuReadLine
	{
		[DllImport("libmonodebuggerreadline")]
		extern static void mono_debugger_readline_init ();

		[DllImport("libmonodebuggerreadline")]
		extern static string mono_debugger_readline_readline (string prompt);

		[DllImport("libmonodebuggerreadline")]
		extern static string mono_debugger_readline_add_history (string line);

		public static GnuReadLine ()
		{
			mono_debugger_readline_init ();
		}

		public GnuReadLine ()
		{
		}

		public string ReadLine (string prompt)
		{
			return mono_debugger_readline_readline (prompt);
		}

		public void AddHistory (string line)
		{
			mono_debugger_readline_add_history (line);
		}
	}
}
