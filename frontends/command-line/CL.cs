//
// Plan:
//    GetProperties en el comando, asigna propiedades, invoca propiedades marcadas.
//

using System;
using System.Text;
using System.Collections;
using System.Reflection;

namespace CL
{
	public class CommandError : Exception
	{
		public CommandError (string message)
			: base (message)
		{ }

		public CommandError (string message, params object[] args)
			: this (String.Format (message, args))
		{ }
	}

	public abstract class Command {
		public ArrayList Args;

		public string Argument {
			get {
				if (Args != null){
					string [] s = (string []) Args.ToArray (typeof (string));
					return String.Join (" ", s);
				} else
					return "";
			}
		}

		public abstract bool Resolve (Engine e, object context);
		
		public abstract string Execute ();
	}

	public class Engine {
		Hashtable commands = new Hashtable ();
		object context;

		public Engine (object context)
		{
			this.context = context;
		}

		public void Register (string s, Type t)
		{
			if (!t.IsSubclassOf (typeof (Command)))
				throw new Exception ("Need a type derived from CL.Command");

			commands [s] = t;
		}
		
		public Command Get (string s, ArrayList args)
		{
			Type t = (Type) commands [s];
			if (t == null)
				return null;

			Command c = (Command) Activator.CreateInstance (t);
			PropertyInfo [] pi = t.GetProperties ();

			int num_args = args != null ? args.Count : 0;
			for (int i = 0; i < num_args; i++){
				string arg = (string) args [i];
				
				if (!arg.StartsWith ("-")){
					if (c.Args == null)
						c.Args = new ArrayList ();
					c.Args.Add (arg);

					for (int j = i+1; j < args.Count; j++)
						c.Args.Add ((string) args [j]);
					break;
				}

				arg = arg.Substring (1);
				for (int j = 0; j < pi.Length; j++){
					if (!pi [j].CanWrite ||
					    (pi [j].Name.ToLower () != arg))
						continue;
					if (pi [j].PropertyType == typeof (bool)){
						pi [j].SetValue (c, true, null);
						goto next;
					}

					if (pi [j].PropertyType == typeof (string)){
						i++;
						if (i >= args.Count){
							Error ("Missing argument to flag: " + arg);
							return null;
						}
						
						pi [j].SetValue (c, args [i], null);
						goto next;
					}

					if (pi [j].PropertyType == typeof (int)){
						i++;

						if (i >= args.Count){
							Error ("Missing integer to flag: " + arg);
							return null;
						}

						try {
							pi [j].SetValue (c, Int32.Parse ((string) args [i]), null);
						} catch {
							Error ("Expected number, got: `{0}'", args [i]);
							return null;
						}
						goto next;
					}
				}
				if (c.Args == null)
					c.Args = new ArrayList ();
				c.Args.Add (args [i]);
			next:
				;
			}
			
			return c;
		}

		public void Error (string fmt, params object [] args)
		{
			Console.WriteLine (String.Format ("Error: " + fmt, args));
		}
		
		public void Run (string s, ArrayList args)
		{
			Command c = Get (s, args);
			if (c == null) {
				Console.WriteLine ("No such command `{0}'.", s);
				return;
			}

			if (!c.Resolve (this, context))
				return;

			try {
				c.Execute ();
			} catch (CommandError e) {
				Console.WriteLine (e);
			}
		}
	}

	public class LineParser {
		StringBuilder sb;
		Engine engine;
		int top;

		string command;
		ArrayList args;
		
		//
		// Command Operations
		//
		public LineParser (Engine e)
		{
			engine = e;
			sb = new StringBuilder ();
		}

		public void Reset ()
		{
			sb.Length = 0;
		}
		
		public void Append (string s)
		{
			sb.Append (s);
		}

		//
		// Parser
		//
		bool SkipSpace (ref int p)
		{
			while (p < top && Char.IsWhiteSpace (sb [p]))
				p++;

			return true;
		}
		
		bool GetCommand (ref int p)
		{
			SkipSpace (ref p);
			
			if (p >= top)
				return false;

			if (Char.IsLetter (sb [p])){
				int start = p++;
				while (p < top && Char.IsLetterOrDigit (sb [p])){
					p++;
				}
				command = sb.ToString (start, p-start);
				return true;
			}
			return false;
		}

		void AddArg (string s)
		{
			if (args == null)
				args = new ArrayList ();
			args.Add (s);
		}
		
		// Parses a string, p points to the opening quote
		bool GetString (ref int p)
		{
			p++;
			if (p >= top)
				return false;

			StringBuilder helper = new StringBuilder ();
			while (p < top){
				char c = sb [p];
				p++;
				
				if (c == '"'){
					AddArg (helper.ToString ());
					return true;
				}

				if (c == '\\'){
				} else
					helper.Append (c);
			}
			return false;
		}
		
		bool GetArgument (ref int p)
		{
			char c;
			SkipSpace (ref p);
			if (p >= top)
				return true;
			c = sb [p];
			if (c == '"')
				return GetString (ref p);

			if (c == '{'){
				//return GetBlock (ref p);
			}

			StringBuilder helper = new StringBuilder ();
			while (p < top){
				c = sb [p++];

				if (Char.IsWhiteSpace (c))
					break;
				helper.Append (c);
			}
			AddArg (helper.ToString ());
			return true;
		}
		
		bool Parse ()
		{
			top = sb.Length;
			int p = 0;

			if (args != null)
				args.Clear ();
			if (GetCommand (ref p)){
				while (p < top){
					if (!GetArgument (ref p))
						return false;
				}
			} else
				return false;
			return true;
		}
		
		public bool IsComplete ()
		{
			return Parse ();
		}

		public void Execute ()
		{
			if (Parse ()){
				engine.Run (command, args); 
			}
		}
	}
}
