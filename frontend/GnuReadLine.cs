using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Frontend
{
	public delegate void CompletionDelegate (string text, int start, int end);

	internal class GnuReadLine
	{
		[DllImport("libmonodebuggerreadline")]
		extern static bool mono_debugger_readline_static_init ();

		[DllImport("libmonodebuggerreadline")]
		extern static int mono_debugger_readline_is_a_tty (int fd);

		[DllImport("libmonodebuggerreadline")]
		extern static string mono_debugger_readline_readline (string prompt);

		[DllImport("libmonodebuggerreadline")]
		extern static void mono_debugger_readline_add_history (string line);

		[DllImport("libmonodebuggerreadline")]
		extern static void mono_debugger_readline_enable_completion (Delegate handler);

		[DllImport("libmonodebuggerreadline")]
		extern static string mono_debugger_readline_current_line_buffer ();

		[DllImport("libmonodebuggerreadline")]
		extern static void mono_debugger_readline_set_completion_matches (string[] matches, int count);

		static bool has_readline;
		CompletionDelegate completion_handler;

		static GnuReadLine ()
		{
			has_readline = mono_debugger_readline_static_init ();
		}

		protected GnuReadLine () {
		}

		public static bool IsTerminal (int fd)
		{
			return mono_debugger_readline_is_a_tty (fd) != 0;
		}

		public string ReadLine (string prompt)
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

		public void SetCompletionMatches (string[] matches) {
			mono_debugger_readline_set_completion_matches (matches, matches.Length);
		}

		public void EnableCompletion (CompletionDelegate handler)
		{
			completion_handler = handler;
			mono_debugger_readline_enable_completion (handler);
		}

		public static string CurrentLine {
			get {
				return mono_debugger_readline_current_line_buffer ();
			}
		}

		static GnuReadLine instance;
		public static GnuReadLine Instance () {
			if (instance == null)
				instance = new GnuReadLine ();

			return instance;
		}

	}
}
