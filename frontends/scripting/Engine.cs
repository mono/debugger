using System;
using System.Text;
using System.Collections;
using System.Reflection;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.CommandLine
{
	[AttributeUsage (AttributeTargets.Class)]
	public class CommandAttribute : Attribute
	{
		string name;
		string short_description;
		string help_text;

		public CommandAttribute (string name, string short_description)
			: this (name, short_description, null)
		{ }

		public CommandAttribute (string name, string short_description, string help_text)
		{
			this.name = name;
			this.short_description = short_description;
			this.help_text = help_text;
		}

		public string Name {
			get { return name; }
		}

		public string ShortDescription {
			get { return short_description; }
		}

		public string HelpText {
			get { return help_text; }
		}
	}

	[AttributeUsage (AttributeTargets.Property)]
	public class ArgumentAttribute : Attribute
	{
		ArgumentType type;
		string name;
		string short_description;
		string help_text;

		public ArgumentAttribute (ArgumentType type, string name, string descr)
			: this (type, name, descr, null)
		{ }

		public ArgumentAttribute (ArgumentType type, string name, string descr,
					  string help)
		{
			this.type = type;
			this.name = name;
			this.short_description = descr;
			this.help_text = help;
		}

		public ArgumentType Type {
			get { return type; }
		}

		public string Name {
			get { return name; }
		}

		public string ShortDescription {
			get { return short_description; }
		}

		public string HelpText {
			get { return help_text; }
		}
	}

	[AttributeUsage (AttributeTargets.Class)]
	public class ExpressionAttribute : Attribute
	{ 
		string name;
		string short_description;
		string help_text;

		public ExpressionAttribute (string name, string short_description)
			: this (name, short_description, null)
		{ }

		public ExpressionAttribute (string name, string short_description, string help_text)
		{
			this.name = name;
			this.short_description = short_description;
			this.help_text = help_text;
		}

		public string Name {
			get { return name; }
		}

		public string ShortDescription {
			get { return short_description; }
		}

		public string HelpText {
			get { return help_text; }
		}
	}

	public enum ArgumentType
	{
		Integer,
		String,
		Flag,
		Process,
		Frame
	}

	public class CommandGroup
	{
		public readonly CommandGroup Parent;
		public readonly string Name;

		public CommandGroup (string name)
		{
			this.Name = name;
		}

		public CommandGroup (CommandGroup parent, string name)
			: this (name)
		{
			this.Parent = parent;

			if (parent.Name != "")
				Name = parent.Name + " " + name;
			else
				Name = name;
		}

		public string MakeName (string id)
		{
			if (Name != "")
				return Name + " " + id;
			else
				return id;
		}

		Hashtable commands = new Hashtable ();
		
		public void Register (string name, object o)
		{
			commands.Add (name, o);
		}

		public object Lookup (string name)
		{
			return commands [name];
		}

		public override string ToString ()
		{
			StringBuilder sb = new StringBuilder ();
			bool first = true;
			foreach (string name in commands.Keys) {
				if (first)
					first = false;
				else
					sb.Append (",");
				sb.Append (name);
			}
			return sb.ToString ();
		}
	}

	public class Engine
	{
		ScriptingContext context;

		public Engine (ScriptingContext context)
		{
			this.context = context;
		}

		static CommandGroup root;
		static Type command_type = typeof (Command);
		static Type expression_type = typeof (Expression);

		static Engine ()
		{
			root = new CommandGroup ("");

			foreach (Type type in command_type.Assembly.GetTypes ()) {
				if (!type.IsSubclassOf (command_type) || type.IsAbstract)
					continue;

				object[] attrs = type.GetCustomAttributes (
					typeof (CommandAttribute), true);

				if (attrs.Length != 1) {
					Console.WriteLine ("Ignoring command `{0}'", type);
					continue;
				}

				CommandAttribute attr = (CommandAttribute) attrs [0];
				RegisterCommand (root, attr.Name, type);
			}
		}

		public static void RegisterCommand (CommandGroup group, string name, Type type)
		{
			int pos = name.IndexOf (' ');
			if (pos < 0) {
				group.Register (name, type);
				return;
			}

			string first = name.Substring (0, pos);
			string last = name.Substring (pos + 1);

			CommandGroup first_group = (CommandGroup) group.Lookup (first);
			if (first_group == null) {
				first_group = new CommandGroup (group, first);
				group.Register (first, first_group);
			}

			RegisterCommand (first_group, last, type);
		}

		public static CommandGroup Root {
			get { return root; }
		}

		public PropertyInfo GetParameter (Command command, string name,
						  out ArgumentAttribute attr)
		{
			Type type = command.GetType ();
			foreach (PropertyInfo pi in type.GetProperties ()) {
				if (!pi.CanWrite)
					continue;

				object[] attrs = pi.GetCustomAttributes (
					typeof (ArgumentAttribute), true);

				if (attrs.Length != 1)
					continue;

				attr = (ArgumentAttribute) attrs [0];
				if (attr.Name != name)
					continue;

				return pi;
			}

			attr = null;
			return null;
		}
	}
}
