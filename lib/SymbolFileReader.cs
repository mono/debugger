using System;
using System.Reflection;
using System.Collections;
using Mono.GetOptions;
using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	internal struct MethodSearch
	{
		public readonly string Name;
		public readonly int Closure;

		public MethodSearch (string name, int closure)
		{
			this.Name = name;
			this.Closure = closure;
		}
	}

	internal class MyOptions : Options
	{
		ArrayList methods = null;
		ArrayList sources = null;
		ArrayList tokens = null;
		int current_closure = -1;

		[Option("Find a method", 'm')]
		public WhatToDoNext method (string value)
		{
			if (methods == null)
				methods = new ArrayList ();
			methods.Add (new MethodSearch (value, current_closure));
			return WhatToDoNext.GoAhead;
		}

		[Option("Next method lookup happens in this method", 'c')]
		public WhatToDoNext closure (int value)
		{
			current_closure = value;
			return WhatToDoNext.GoAhead;
		}

		[Option("Get method by token", 't')]
		public WhatToDoNext token (int value)
		{
			if (tokens == null)
				tokens = new ArrayList ();
			tokens.Add (value);
			return WhatToDoNext.GoAhead;
		}

		public bool HasMethods {
			get { return methods != null; }
		}

		public MethodSearch[] Methods {
			get {
				MethodSearch[] retval = new MethodSearch [methods.Count];
				methods.CopyTo (retval, 0);
				return retval;
			}
		}

		public bool HasTokens {
			get { return tokens != null; }
		}

		public int[] Tokens {
			get {
				int[] retval = new int [tokens.Count];
				tokens.CopyTo (retval, 0);
				return retval;
			}
		}

		[Option("Find a source file", 's')]
		public WhatToDoNext source (string value)
		{
			if (sources == null)
				sources = new ArrayList ();
			sources.Add (value);
			return WhatToDoNext.GoAhead;
		}

		public bool HasSources {
			get { return sources != null; }
		}

		public string[] Sources {
			get {
				string[] retval = new string [sources.Count];
				sources.CopyTo (retval, 0);
				return retval;
			}
		}
	}

	internal class SymbolFileReader
	{
		MonoSymbolFile file;
		Assembly assembly;

		public SymbolFileReader (string file_name)
		{
			assembly = Assembly.LoadFrom (file_name);
			file = MonoSymbolFile.ReadSymbolFile (assembly);
			if (file == null) {
				Console.WriteLine ("Assembly doesn't contain any debugging info.");
				Environment.Exit (1);
			}
		}

		public void FindMethod (MethodSearch method)
		{
			DateTime start = DateTime.Now;
			MethodEntry closure = null;
			if (method.Closure != -1) {
				closure = file.GetMethod (method.Closure);
				Console.WriteLine ("CLOSURE: {0}", closure);
			}
			int index = file.FindMethod (method.Name);
			TimeSpan time = DateTime.Now - start;
			Console.WriteLine ("FIND METHOD: {0} {1} {2}", method, index, time);
		}

		public void FindMethod (int token)
		{
			DateTime start = DateTime.Now;
			MethodEntry method = file.GetMethodByToken (token);
			TimeSpan time = DateTime.Now - start;
			Console.WriteLine ("FIND METHOD: {0} {1} {2}", token, method, time);
		}

		public void FindSource (string source)
		{
			DateTime start = DateTime.Now;
			int index = file.FindSource (source);
			TimeSpan time = DateTime.Now - start;
			Console.WriteLine ("FIND SOURCE: {0} {1} {2}", source, index, time);
		}

		void DumpSource (SourceFileEntry source)
		{
			Console.WriteLine (source);

			foreach (MethodSourceEntry entry in source.Methods)
				;
		}

		void DumpSources ()
		{
			foreach (SourceFileEntry source in file.Sources)
				DumpSource (source);
		}

		void DumpMethod (MethodEntry method)
		{
			Console.WriteLine (method);
		}

		void DumpMethods ()
		{
			foreach (MethodEntry method in file.Methods)
				DumpMethod (method);
		}

		void Dump ()
		{
			Console.WriteLine ("Symbol file contains {0} sources, {1} methods and {2} types.",
					   file.SourceCount, file.MethodCount, file.TypeCount);

			Console.WriteLine ("Dumping sources");
			DumpSources ();

			Console.WriteLine ("Dumping methods.");
			DumpMethods ();
		}

		static void Main (string[] args)
		{
			MyOptions options = new MyOptions ();
			options.ParsingMode = OptionsParsingMode.Linux;
			options.ProcessArgs (args);

			if (options.RemainingArguments.Length != 1) {
				Console.WriteLine ("Filename argument expected.");
				Environment.Exit (1);
			}

			string file_name = options.RemainingArguments [0];
			SymbolFileReader reader = new SymbolFileReader (file_name);

			bool done_something = false;

			if (options.HasTokens) {
				foreach (int token in options.Tokens)
					reader.FindMethod (token);
				done_something = true;
			}

			if (options.HasMethods) {
				foreach (MethodSearch method in options.Methods)
					reader.FindMethod (method);
				done_something = true;
			}

			if (options.HasSources) {
				foreach (string source in options.Sources)
					reader.FindSource (source);
				done_something = true;
			}

			if (!done_something)
				reader.Dump ();
		}
	}
}
