using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Frontend
{
	public delegate void CompletionDelegate (string text, int start, int end);
	public delegate string CompletionGenerator (string text, int state);

	internal class GnuReadLine
	{
		[DllImport("libmonodebuggerreadline")]
		extern static void mono_debugger_readline_static_init ();

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
		extern static int mono_debugger_readline_get_columns ();

		[DllImport("libmonodebuggerreadline")]
		extern static void mono_debugger_readline_set_completion_matches (string[] matches, int count);

		[DllImport("libmonodebuggerreadline")]
		extern static int mono_debugger_readline_get_filename_completion_desired ();

		[DllImport("libmonodebuggerreadline")]
		extern static void mono_debugger_readline_set_filename_completion_desired (int v);

		static CompletionDelegate completion_handler;

		static GnuReadLine ()
		{
			mono_debugger_readline_static_init ();
		}

		public static bool IsTerminal (int fd)
		{
			return mono_debugger_readline_is_a_tty (fd) != 0;
		}

		public static string ReadLine (string prompt)
		{
			return mono_debugger_readline_readline (prompt);
		}

		public static void AddHistory (string line)
		{
			mono_debugger_readline_add_history (line);
		}

		public static int Columns {
			get {
				return mono_debugger_readline_get_columns ();
			}
		}

		public static void SetCompletionMatches (string[] matches)
		{
			mono_debugger_readline_set_completion_matches (matches, matches == null ? 0 : matches.Length);
		}

		public static void EnableCompletion (CompletionDelegate handler)
		{
			completion_handler = handler;
			mono_debugger_readline_enable_completion (handler);
		}

		public static string CurrentLine
		{
			get {
				return mono_debugger_readline_current_line_buffer ();
			}
		}

		public static bool FilenameCompletionDesired
		{
			get {
				return mono_debugger_readline_get_filename_completion_desired () == 0 ? false : true;
			}
			set {
				mono_debugger_readline_set_filename_completion_desired (value == true ? 1 : 0);
			}
		}

		static GnuReadLine instance;
		public static GnuReadLine Instance ()
		{
			if (instance == null)
				instance = new GnuReadLine ();

			return instance;
		}

	}
}
