using System;
using System.Reflection;
using System.Collections;
using Mono.GetOptions;
using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	internal class MyOptions : Options
	{
		ArrayList methods = null;
		ArrayList sources = null;

		[Option("Find a method", 'm')]
		public WhatToDoNext method (string value)
		{
			if (methods == null)
				methods = new ArrayList ();
			methods.Add (value);
			return WhatToDoNext.GoAhead;
		}

		public bool HasMethods {
			get { return methods != null; }
		}

		public string[] Methods {
			get {
				string[] retval = new string [methods.Count];
				methods.CopyTo (retval, 0);
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

	public class SymbolFileReader
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

		public void FindMethod (string method)
		{
			DateTime start = DateTime.Now;
			int index = file.FindMethod (method);
			TimeSpan time = DateTime.Now - start;
			Console.WriteLine ("FIND METHOD: {0} {1} {2}", method, index, time);
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
			for (int i = 0; i < file.SourceCount; i++) {
				SourceFileEntry source = file.GetSourceFile (i + 1);

				DumpSource (source);
			}
		}

		void DumpMethod (MethodEntry method)
		{
			Console.WriteLine (method);
		}

		void DumpMethods ()
		{
			for (int i = 0; i < file.MethodCount; i++) {
				MethodEntry method = file.GetMethod (i + 1);

				DumpMethod (method);
			}
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

			if (options.HasMethods) {
				foreach (string method in options.Methods)
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
