using System;
using System.Collections;
using System.IO;
using System.Reflection;
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
				if (c != null) {
					c.Complete (engine, text, start, end);
				}
			}
		}

		string ComputeMCP (ArrayList choices, string initial_prefix)
		{
			string s = (string)choices[0];
			int maxlen = s.Length;

			for (int i = 1; i < choices.Count; i ++) {
				if (maxlen > ((string)choices[i]).Length)
					maxlen = ((string)choices[i]).Length;
			}
			s = s.Substring (0, maxlen);

			for (int l = initial_prefix.Length; l < maxlen; l ++) {
				for (int i = 1; i < choices.Count; i ++) {
					string test = (string)choices[i];
					if (test[l] != s[l])
						return s.Substring (0, l);
				}
			}

			return s;
		}

		/*
		 * some prebuilt completers, for use by commands.
		 */

		/* CommandCompleter: completes against the list of commands
		 * (but not aliases) that have been registered with the
		 * engine.  This is used by the CompletionHandler itself to
		 * complete commands at the beginning of the line. */
		public void CommandCompleter (string text, int start, int end)
		{ 
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
					matched_commands.Insert (0, ComputeMCP (matched_commands, text));
				}

				match_strings = new string [matched_commands.Count + 1];
				matched_commands.CopyTo (match_strings);
				match_strings [matched_commands.Count] = null;
			}

			GnuReadLine.SetCompletionMatches (match_strings);
		}

		public void StringsCompleter (string[] haystack, string text, int start, int end)
		{
			ArrayList matches = new ArrayList();

			foreach (string s in haystack) {
				if (s.ToLower().StartsWith (text))
					matches.Add (s.ToLower());
			}

			string[] match_strings = null;

			if (matches.Count > 0) {
				if (matches.Count > 1) {
					/* always add the prefix at
					 * the beginning when we have
					 * > 1 matches, so that
					 * readline will display the
					 * matches. */
					matches.Insert (0, ComputeMCP (matches, text));
				}

				match_strings = new string [matches.Count + 1];
				matches.CopyTo (match_strings);
				match_strings [matches.Count] = null;
			}

			GnuReadLine.SetCompletionMatches (match_strings);
		}

		public void ArgumentCompleter (Type t, string text, int start, int end)
		{
			ArrayList matched_args = new ArrayList();
			PropertyInfo [] pi = t.GetProperties ();

			foreach (PropertyInfo p in pi) {
				if (!p.CanWrite)
					continue;
				if (text == "-" ||
				    p.Name.ToLower().StartsWith (text.Substring (1))) {
					matched_args.Add ("-" + p.Name.ToLower());
				}
			}

			string[] match_strings = null;

			if (matched_args.Count > 0) {
				if (matched_args.Count > 1) {
					/* always add the prefix at
					 * the beginning when we have
					 * > 1 matches, so that
					 * readline will display the
					 * matches. */
					matched_args.Insert (0, ComputeMCP (matched_args, text));
				}

				match_strings = new string [matched_args.Count + 1];
				matched_args.CopyTo (match_strings);
				match_strings [matched_args.Count] = null;
			}

			GnuReadLine.SetCompletionMatches (match_strings);
		}

		public void FilenameCompleter (string text, int start, int end)
		{
			string dir;
			string file_prefix;
			DebuggerEngine de = engine as DebuggerEngine;

			GnuReadLine.FilenameCompletionDesired = true;

			if (text.IndexOf (Path.DirectorySeparatorChar) == -1) {
				dir = de.Interpreter.Options.WorkingDirectory;
				file_prefix = text;
			}
			else {
				dir = text.Substring (0, text.LastIndexOf (Path.DirectorySeparatorChar) + 1);
				file_prefix = text.Substring (text.LastIndexOf (Path.DirectorySeparatorChar) + 1);
			}

			string[] fs_entries;

			try {
				fs_entries = Directory.GetFileSystemEntries (dir, file_prefix + "*");
			}
			catch (Exception e) {
				GnuReadLine.SetCompletionMatches (null);
				return;
			}

			ArrayList matched_paths = new ArrayList();
			foreach (string f in fs_entries) {
				if (f.StartsWith (dir + file_prefix)) {
					matched_paths.Add (f);
				}
			}

			string[] match_strings = null;

			if (matched_paths.Count > 0) {
				if (matched_paths.Count > 1) {
					/* always add the prefix at
					 * the beginning when we have
					 * > 1 matches, so that
					 * readline will display the
					 * matches. */
					matched_paths.Insert (0, ComputeMCP (matched_paths, text));
				}

				match_strings = new string [matched_paths.Count + 1];
				matched_paths.CopyTo (match_strings);
				match_strings [matched_paths.Count] = null;
			}

			GnuReadLine.SetCompletionMatches (match_strings);

		}

		/* NoopCompleter: always returns an empty list (no
		 * matches). */
	  	public void NoopCompleter (string text, int start, int end)
		{
			GnuReadLine.SetCompletionMatches (null);
		}

		public void SymbolCompleter (string text, int start, int end)
		{
			DebuggerEngine de = engine as DebuggerEngine;

			try {
				ArrayList method_list = new ArrayList ();
				string[] namespaces = de.Interpreter.GlobalContext.GetNamespaces();

				foreach (Module module in de.Interpreter.Modules) {
					if (module.SymbolFile == null) {
						// use the module's symbol table and
						// symbol ranges to add methods

						if (!module.SymbolsLoaded || !module.SymbolTable.HasMethods) {
							continue;
						}

						foreach (ISymbolRange range in module.SymbolTable.SymbolRanges) {
							IMethod method = range.SymbolLookup.Lookup (range.StartAddress);
							if (method != null) {
								if (method.Name.StartsWith (text)) {
									method_list.Add (method.Name);
								}
								if (namespaces != null) {
									foreach (string n in namespaces) {
										if (n != "" && method.Name.StartsWith (String.Concat (n, ".", text)))
											method_list.Add (method.Name.Substring (n.Length + 1));
									}
								}
							}
						}
					}
					else {
						// use the module's source
						// files to add methods.
						// there has to be a more
						// efficient way to do this...
						if (module.SymbolFile.Sources == null) {
							continue;
						}

						foreach (SourceFile sf in module.SymbolFile.Sources) {
							foreach (SourceMethod method in sf.Methods) {
								if (method.Name.StartsWith (text)) {
									int parameter_start = method.Name.IndexOf ('(');
									if (parameter_start != -1)
										method_list.Add (method.Name.Substring (0, parameter_start));
									else
										method_list.Add (method.Name);
								}
								if (namespaces != null) {
									foreach (string n in namespaces) {
										if (n != "" && method.Name.StartsWith (String.Concat (n, ".", text))) {
											int parameter_start = method.Name.IndexOf ('(');
											if (parameter_start != -1)
												method_list.Add (method.Name.Substring (n.Length + 1,
																	parameter_start - n.Length - 1));
											else
												method_list.Add (method.Name.Substring (n.Length + 1));
										}
									}
								}
							}
						}
					}
				}

				string[] methods = null;
				if (method_list.Count > 0) {
					method_list.Insert (0, ComputeMCP (method_list, text));
					methods = new string [method_list.Count + 1];
					method_list.CopyTo (methods);
					methods [method_list.Count] = null;
				}

				GnuReadLine.SetCompletionMatches (methods);
			}
			catch (Exception e) {
				GnuReadLine.SetCompletionMatches (null);
			}
		}
	}
}
