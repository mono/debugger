using System;
using System.IO;
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

		string prompt;

		public static GnuReadLine ()
		{
			mono_debugger_readline_init ();
		}

		public GnuReadLine (string prompt)
		{
			this.prompt = prompt;
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

	public class GnuReadLineReader : TextReader
	{
		GnuReadLine readline;

		public GnuReadLineReader (GnuReadLine readline)
		{
			this.readline = readline;
		}

		bool closed = false;
		string current_line = null;
		int pos = 0;

		bool check_line ()
		{
			if (closed)
				return false;

		again:
			if (current_line == null) {
				current_line = readline.ReadLine ();
				if (current_line == null)
					return false;

				pos = 0;
				current_line = current_line.TrimStart (' ', '\t');
				current_line = current_line.TrimEnd (' ', '\t');
				if (current_line == "")
					goto again;

				readline.AddHistory (current_line);
				current_line = current_line + '\n';
			}

			if (pos >= current_line.Length) {
				current_line = null;
				goto again;
			}

			return true;
		}

		public override int Peek ()
		{
			if (!check_line ())
				return -1;

			return current_line [pos];
		}

		public override int Read ()
		{
			if (!check_line ())
				return -1;

			return current_line [pos++];
		}

		public override string ReadLine ()
		{
			string retval;

			if (!check_line ())
				return String.Empty;

			retval = current_line;
			current_line = null;
			return retval;
		}

		public override string ReadToEnd ()
		{
			return ReadLine ();
		}

		public void Discard ()
		{
			current_line = null;
		}

		public override void Close ()
		{
			current_line = null;
			closed = true;
			base.Close ();
		}
	}
}
