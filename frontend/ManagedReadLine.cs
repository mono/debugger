using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace Mono.Debugger.Frontend
{
	internal class ManagedReadLine : IReadLine
	{
		Stack<string> history = new Stack<string> ();
		string current_line = string.Empty;

		public bool IsTerminal (int fd) {
			return false;
		}

		public string ReadLine (string prompt) {
			Console.Write (prompt);
			current_line = Console.ReadLine ();
			return current_line;
		}

		public void AddHistory (string line) {
			history.Push (line);
		}

		public int Columns {
			get {
				throw new NotImplementedException ();
			}
		}

		public void SetCompletionMatches (string[] matches) {
			throw new NotImplementedException ();
		}

		public void EnableCompletion (CompletionDelegate handler) {
			throw new NotImplementedException ();
		}

		public string CurrentLine {
			get {
				return current_line;
			}
		}

		public bool FilenameCompletionDesired {
			get {
				throw new NotImplementedException ();
			}
			set {
				throw new NotImplementedException ();
			}
		}
	}
}

