using System;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Frontend
{
	public class Completer {
		private Engine engine;

		public Completer (Engine engine) {
			this.engine = engine;
		}

		/* This method gets installed as the GnuReadLine completion
		 * delegate.  It completes commands at the start of the
		 * line, and does command specific completion for
		 * arguments. */
		public void CompletionHandler (string text, int start, int end) {
			if (start == 0) {
				CommandCompleter (text, start, end);
			}
			else {
				/* we look up the command in the
				 * current line buffer and call that
				 * command's completion generator to
				 * generate the list of strings. 
				 */

				string line = GnuReadLine.CurrentLine;
				string command;

				int ptr = 0;
				while (!Char.IsWhiteSpace (line[ptr]))
					++ptr;

				command = line.Substring (0, ptr);
				Command c = engine.Get (command, null);
				c.Complete (engine, text, start, end);
			}
		}

		/*
		 * some prebuilt completers, for use by commands.
		 */

		/* CommandCompleter: completes against the list of commands
		 * (but not aliases) that have been registered with the
		 * engine.  This is used by the CompletionHandler itself to
		 * complete commands at the beginning of the line. */
		public void CommandCompleter (string text, int start, int end) {
			/* complete possible commands */
			ArrayList matched_commands = new ArrayList();
			string[] match_strings = null;

			foreach (string key in engine.Commands.Keys) {
				if (key.StartsWith (text))
					matched_commands.Add (key);
			}


			if (matched_commands.Count > 0) {
				if (matched_commands.Count > 1) {
					/* always add the prefix at
					 * the beginning when we have
					 * > 1 matches, so that
					 * readline will display the
					 * matches. */
					matched_commands.Insert (0, text);
				}

				match_strings = new string [matched_commands.Count + 1];
				matched_commands.CopyTo (match_strings);
				match_strings [matched_commands.Count] = null;
			}

			GnuReadLine.Instance().SetCompletionMatches (match_strings);
		}


		/* NoopCompleter: always returns an empty list (no
		 * matches). */
	  	public void NoopCompleter (string text, int start, int end) {
			GnuReadLine.Instance().SetCompletionMatches (null);
		}
	}
}
