using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Frontend
{
	internal class LineReader
	{
		static IReadLine readline;

		static LineReader () {
			// Hack for now. Put this check in a better place
			// where all can access it.
			if ((int)Environment.OSVersion.Platform < 4)
				readline = new ManagedReadLine ();
			else
				readline = new GnuReadLine ();
		}

		public static bool IsTerminal (int fd) {
			return readline.IsTerminal (fd);
		}

		public static string ReadLine (string prompt) {
			return readline.ReadLine (prompt);
		}

		public static void AddHistory (string line) {
			readline.AddHistory (line);
		}

		public static int Columns {
			get {
				return readline.Columns;
			}
		}

		public static void SetCompletionMatches (string[] matches) {
			readline.SetCompletionMatches (matches);
		}

		public static void EnableCompletion (CompletionDelegate handler) {
			readline.EnableCompletion (handler);
		}

		public static string CurrentLine {
			get {
				return readline.CurrentLine;
			}
		}

		public static bool FilenameCompletionDesired {
			get {
				return readline.FilenameCompletionDesired;
			}
			set {
				readline.FilenameCompletionDesired = value;
			}
		}
	}

	internal interface IReadLine
	{
		bool IsTerminal (int fd);

		string ReadLine (string prompt);

		void AddHistory (string line);

		int Columns {
			get;
		}

		void SetCompletionMatches (string[] matches);

		void EnableCompletion (CompletionDelegate handler);

		string CurrentLine {
			get;
		}

		bool FilenameCompletionDesired {
			get;
			set;
		}
	}
}

