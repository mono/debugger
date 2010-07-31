using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Frontend
{
	using Mono.Terminal;

	public class Completer {
		private Engine engine;

		public Completer (Engine engine) {
			this.engine = engine;
		}

		public LineEditor.Completion Complete (string text, int start)
		{
			if (start == 0)
				return new LineEditor.Completion ("", CommandCompleter (text));

			string command;

			int ptr = 0;
			while (!Char.IsWhiteSpace (text[ptr]))
				++ptr;

			command = text.Substring (0, ptr);
			text = text.Substring (ptr + 1);

			Command c = engine.Get (command, null);
			if (c == null)
				return null;

			var completion = c.Complete (engine, text);
			if (completion == null)
				return null;

			var choices = new List<string> (completion);

			string prefix = ComputeMCP (choices, text);
			var results = choices.Select (k => k.Substring (prefix.Length)).ToArray ();

			return new LineEditor.Completion (prefix, results);
		}

		string ComputeMCP (List<string> choices, string initial_prefix)
		{
			string s = choices[0];
			int maxlen = s.Length;

			for (int i = 1; i < choices.Count; i ++) {
				if (maxlen > choices[i].Length)
					maxlen = choices[i].Length;
			}
			s = s.Substring (0, maxlen);

			for (int l = initial_prefix.Length; l < maxlen; l++) {
				for (int i = 1; i < choices.Count; i ++) {
					string test = choices[i];
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
		public string[] CommandCompleter (string text)
		{ 
			/* complete possible commands */
			var matches = new List<string> ();
			string[] match_strings = null;

			foreach (string key in engine.Commands.Keys) {
				if (key.StartsWith (text))
					matches.Add (key);
			}

			return matches.ToArray ();
		}

		public string[] StringsCompleter (string[] haystack, string text)
		{
			var matches = new List<string> ();

			foreach (string s in haystack) {
				if (s.ToLower().StartsWith (text))
					matches.Add (s.ToLower());
			}

			return matches.ToArray ();
		}

		public string[] ArgumentCompleter (Type t, string text)
		{
			var matches = new List<string> ();
			PropertyInfo [] pi = t.GetProperties ();

			foreach (PropertyInfo p in pi) {
				if (!p.CanWrite)
					continue;
				if (text == "-" ||
				    p.Name.ToLower().StartsWith (text.Substring (1))) {
					matches.Add ("-" + p.Name.ToLower());
				}
			}

			return matches.ToArray ();
		}

		public string[] FilenameCompleter (string text)
		{
			string dir;
			string file_prefix;
			DebuggerEngine de = engine as DebuggerEngine;

			if (text.IndexOf (Path.DirectorySeparatorChar) == -1) {
				dir = de.Interpreter.Options.WorkingDirectory ?? Environment.CurrentDirectory;
				file_prefix = text;
			}
			else {
				dir = text.Substring (0, text.LastIndexOf (Path.DirectorySeparatorChar) + 1);
				file_prefix = text.Substring (text.LastIndexOf (Path.DirectorySeparatorChar) + 1);
			}

			string[] fs_entries;

			try {
				fs_entries = Directory.GetFileSystemEntries (dir, file_prefix + "*");
			} catch {
				return null;
			}

			var matched_paths = new List<string> ();
			foreach (string f in fs_entries) {
				if (f.StartsWith (Path.Combine (dir, file_prefix))) {
					matched_paths.Add (f);
				}
			}

			return matched_paths.ToArray ();
		}

		/* NoopCompleter: always returns an empty list (no
		 * matches). */
	  	public string[] NoopCompleter (string text)
		{
			return null;
		}

		public string[] SymbolCompleter (ScriptingContext context, string text)
		{
			try {
				var method_list = new List<string> ();
				string[] namespaces = context.GetNamespaces();
				Module[] modules = context.CurrentProcess.Modules;

				foreach (Module module in modules) {
					if (!module.SymbolsLoaded || !module.SymbolTable.HasMethods)
						continue;

					SourceFile[] sources = module.Sources;
					if (sources == null)
						continue;

					foreach (SourceFile sf in sources) {
						foreach (MethodSource method in sf.Methods) {
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

				return method_list.ToArray ();
			} catch {
				return null;
			}
		}
	}
}
