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
		Process,
		Frame
	}


	public class Engine
	{
		ScriptingContext context;

		public Engine (ScriptingContext context)
		{
			this.context = context;
		}

		static Hashtable commands;
		static Type command_type = typeof (Command);
		static Type expression_type = typeof (Expression);

		static Engine ()
		{
			commands = new Hashtable ();

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
				commands.Add (attr.Name, type);
			}
		}

		public Command GetCommand (string name)
		{
			Type t = (Type) commands [name];
			if (t == null)
				return null;

			try {
				return (Command) Activator.CreateInstance (t);
			} catch (Exception ex) {
				Console.WriteLine ("ERROR: {0}", ex);
				return null;
			}
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
