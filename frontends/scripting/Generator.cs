using System;
using Math = System.Math;
using System.Text;
using System.Collections;
using System.Reflection;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.CommandLine
{
	[AttributeUsage (AttributeTargets.Class)]
	public class CommandAttribute : Attribute
	{
		string jay;
		string short_description;
		string help_text;

		public CommandAttribute (string jay, string short_description)
			: this (jay, short_description, null)
		{ }

		public CommandAttribute (string jay, string short_description, string help_text)
		{
			this.jay = jay;
			this.short_description = short_description;
			this.help_text = help_text;
		}

		public string Jay {
			get { return jay; }
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

	public class GeneratorException : Exception
	{
		public GeneratorException (string format, params object[] args)
			: base (String.Format (format, args))
		{ }
	}

	public class CommandGroup
	{
		string name;
		Generator generator;
		Hashtable command_hash = new Hashtable ();
		ArrayList commands = new ArrayList ();
		ArrayList subgroups = new ArrayList ();

		public CommandGroup (Generator generator, string name)
		{
			this.generator = generator;
			this.name = name;
		}

		public string Name {
			get { return name; }
		}

		public void AddCommand (CommandClass command, string[] tokens)
		{
			string token = tokens [0];
			Entry entry = (Entry) command_hash [token];

			if (entry == null) {
				if (tokens.Length > 1) {
					CommandGroup group = new CommandGroup (generator, token);
					subgroups.Add (group);
					entry = new Entry (token, group);
				} else
					entry = new Entry (token);

				command_hash.Add (token, entry);
			}

			if (tokens.Length > 1) {
				string[] new_tokens = new string [tokens.Length - 1];
				Array.Copy (tokens, 1, new_tokens, 0, new_tokens.Length);

				entry.Group.AddCommand (command, new_tokens);
			} else {
				entry.AddCommand (command);
				commands.Add (command);
			}
		}

		public void PrintHelp (ScriptingContext context)
		{
			string[] names = new string [commands.Count];

			int max = 0;
			for (int i = 0; i < commands.Count; i++) {
				CommandClass command = (CommandClass) commands [i];

				names [i] = String.Join (" ", command.Tokens).ToLower ();
				max = Math.Max (max, names [i].Length);
			}

			for (int i = 0; i < commands.Count; i++)
				names [i] = names [i].PadRight (max);

			context.Print ("Commands:");
			context.Print ("");

			for (int i = 0; i < commands.Count; i++) {
				CommandClass command = (CommandClass) commands [i];

				context.Print ("  {0}   {1}", names [i], command.ShortDescription);
			}

			context.Print ("");
			context.Print ("Type `help' followed by a command name to get more info.");

			if (subgroups.Count == 0)
				return;

			context.Print ("The following commands have sub-commands; type `help' followed");
			context.Print ("by the command name to get more information.");
			context.Print ("");

			string[] group_names = new string [subgroups.Count];

			for (int i = 0; i < subgroups.Count; i++) {
				CommandGroup group = (CommandGroup) subgroups [i];

				group_names [i] = group.Name.ToLower ();
			}

			context.Print ("  {0}", String.Join (" ", group_names));
			context.Print ("");
		}

		public void PrintHelp (ScriptingContext context, string arg_list)
		{
			arg_list = arg_list.TrimStart (' ', '\t');
			arg_list = arg_list.TrimEnd (' ', '\t');

			if (arg_list.Length == 0)
				PrintHelp (context);
			else {
				string[] arguments = arg_list.Split (' ', '\t');
				PrintHelp (context, arguments, 0);
			}
		}

		void PrintHelp (ScriptingContext context, string[] arguments, int pos)
		{
			if (pos >= arguments.Length) {
				PrintHelp (context);
				return;
			}

			string token = arguments [pos].ToUpper ();
			Entry entry = (Entry) command_hash [token];
			if (entry == null) {
				if (pos + 1 < arguments.Length) {
					context.Print ("No such command: `{0}'", arguments [pos]);
					return;
				} else {
					generator.PrintExpressionHelp (context, arguments [pos]);
					return;
				}
			}

			if (entry.IsGroup)
				entry.Group.PrintHelp (context, arguments, pos + 1);
			else {
				foreach (CommandClass command in entry.Commands)
					PrintHelp (context, command);
				context.Print ("");
				context.Print ("To get help about the arguments, type `help' followed by ");
				context.Print ("one of the expression names.");
			}
		}

		void PrintHelp (ScriptingContext context, CommandClass command)
		{
			StringBuilder sb = new StringBuilder ();

			sb.Append ("  ");
			sb.Append (String.Join (" ", command.Tokens).ToLower ());

			foreach (ExpressionClass argument in command.Arguments) {
				sb.Append (" <");
				sb.Append (argument.Name);
				sb.Append (">");
			}

			context.Print ("Command:");
			context.Print (sb.ToString ());
			context.Print ("");
			context.Print (command.ShortDescription);

			if (command.HelpText != null) {
				context.Print ("");
				context.Print (command.HelpText);
			}
		}

		class Entry
		{
			string token;
			ArrayList commands;
			CommandGroup group;

			public Entry (string token, CommandGroup group)
			{
				this.token = token;
				this.group = group;
			}

			public Entry (string token)
			{
				this.token = token;
				commands = new ArrayList ();
			}

			public string Token {
				get { return token; }
			}

			public bool IsGroup {
				get { return group != null; }
			}

			public CommandClass[] Commands {
				get {
					if (commands == null)
						throw new GeneratorException (
							"Token {0} must be a command", token);

					CommandClass[] retval = new CommandClass [commands.Count];
					commands.CopyTo (retval, 0);
					return retval;
				}
			}

			public CommandGroup Group {
				get {
					if (group == null)
						throw new GeneratorException (
							"Token {0} must be a command group", token);

					return group;
				}
			}

			public void AddCommand (CommandClass command)
			{
				if (commands == null)
					throw new GeneratorException (
						"Token {0} must be a command, not a command group", token);

				commands.Add (command);
			}
		}
	}

	public class ExpressionClass
	{
		Type type;
		ExpressionAttribute attribute;

		protected ExpressionClass (Type type, ExpressionAttribute attribute)
		{
			this.type = type;
			this.attribute = attribute;
		}

		public string Name {
			get { return attribute.Name; }
		}

		public string ShortDescription {
			get { return attribute.ShortDescription; }
		}

		public string HelpText {
			get { return attribute.HelpText; }
		}

		public string TypeName {
			get { return type.Name; }
		}

		public static ExpressionClass Create (Type type)
		{
			if (!type.IsSubclassOf (typeof (Expression)))
				return null;

			Attribute[] attributes = Attribute.GetCustomAttributes (
				type, typeof (ExpressionAttribute), false);
			if (attributes.Length != 1)
				return null;

			return new ExpressionClass (type, (ExpressionAttribute) attributes [0]);
		}
	}

	public class CommandClass
	{
		Type type;
		CommandAttribute attribute;
		ExpressionClass[] args;

		protected CommandClass (Type type, CommandAttribute attribute, ExpressionClass[] args)
		{
			this.type = type;
			this.attribute = attribute;
			this.args = args;
		}

		public string Name {
			get {
				return type.Name;
			}
		}

		public string ShortDescription {
			get {
				return attribute.ShortDescription;
			}
		}

		public string HelpText {
			get {
				return attribute.HelpText;
			}
		}

		public ExpressionClass[] Arguments {
			get {
				return args;
			}
		}

		public string[] Tokens {
			get {
				return attribute.Jay.Split (' ');
			}
		}

		public string Format ()
		{
			StringBuilder sb = new StringBuilder ();
			sb.Append (attribute.Jay);

			for (int i = 0; i < args.Length; i++) {
				sb.Append (" ");
				sb.Append (args [i].Name);
			}

			int start = attribute.Jay.Split (' ').Length + 1;

			sb.Append ("\n\t  {\n\t\t$$ = new ");
			sb.Append (type.Name);
			sb.Append (" (");

			for (int i = 0; i < args.Length; i++) {
				if (i != 0)
					sb.Append (", ");
				sb.Append (String.Format (
					"({0}) ${1}", args [i].TypeName, start + i));
			}

			sb.Append (");\n\t  }\n");

			return sb.ToString ();
		}

		public static CommandClass[] Create (Generator generator, Type type)
		{
			if (!type.IsSubclassOf (typeof (Command)))
				return null;

			Attribute[] attributes = Attribute.GetCustomAttributes (
				type, typeof (CommandAttribute), false);
			if (attributes.Length != 1)
				return null;

			MemberInfo[] ctors = type.FindMembers (
				MemberTypes.Constructor, BindingFlags.Public | BindingFlags.Instance,
				new MemberFilter (ctor_filter), null);

			CommandAttribute attribute = (CommandAttribute) attributes [0];

			CommandClass[] retval = new CommandClass [ctors.Length];
			for (int i = 0; i < ctors.Length; i++) {
				ParameterInfo[] param = ((ConstructorInfo) ctors [i]).GetParameters ();
				if (param == null)
					param = new ParameterInfo [0];

				ExpressionClass[] args = new ExpressionClass [param.Length];
				for (int j = 0; j < param.Length; j++)
					args [j] = generator.GetExpression (param [j].ParameterType);

				retval [i] = new CommandClass (type, attribute, args);
			}

			return retval;
		}

		static bool ctor_filter (MemberInfo m, object filter_criteria)
		{
			foreach (ParameterInfo param in ((ConstructorInfo) m).GetParameters ())
				if (!param.ParameterType.IsSubclassOf (typeof (Expression)))
					return false;

			return true;
		}
	}

	public class Generator
	{
		CommandClass[] commands;
		ExpressionClass[] expressions;
		Hashtable expression_hash;
		Hashtable expression_name_hash;
		CommandGroup global_group;

		public Generator (Assembly assembly)
		{
			GetExpressions (assembly);
			GetCommands (assembly);
		}

		public string Format ()
		{
			StringBuilder sb = new StringBuilder ();

			sb.Append ("generated_commands\n");

			for (int i = 0; i < commands.Length; i++) {
				if (i == 0)
					sb.Append ("\t: ");
				else
					sb.Append ("\t| ");
				sb.Append (commands [i].Format ());
			}

			sb.Append ("\t;\n");

			return sb.ToString ();
		}

		public void PrintHelp (ScriptingContext context, string arguments)
		{
			global_group.PrintHelp (context, arguments);
		}

		void GetExpressions (Assembly assembly)
		{
			ArrayList list = new ArrayList ();
			expression_hash = new Hashtable ();
			expression_name_hash = new Hashtable ();

			foreach (Type type in assembly.GetExportedTypes ()) {
				ExpressionClass expression = ExpressionClass.Create (type);
				if (expression != null) {
					list.Add (expression);
					expression_hash.Add (type, expression);
					expression_name_hash.Add (expression.Name, expression);
				}
			}

			expressions = new ExpressionClass [list.Count];
			list.CopyTo (expressions, 0);
		}

		void GetCommands (Assembly assembly)
		{
			ArrayList list = new ArrayList ();

			foreach (Type type in assembly.GetExportedTypes ()) {
				CommandClass[] tmp = CommandClass.Create (this, type);
				if (tmp != null)
					list.AddRange (tmp);
			}

			commands = new CommandClass [list.Count];
			list.CopyTo (commands, 0);

			global_group = new CommandGroup (this, "");

			foreach (CommandClass command in commands)
				global_group.AddCommand (command, command.Tokens);
		}

		internal void PrintExpressionHelp (ScriptingContext context, string name)
		{
			ExpressionClass expression = (ExpressionClass) expression_name_hash [name];
			if (expression == null) {
				context.Print ("No such command or expression: `{0}'", name);
				return;
			}

			context.Print (expression.ShortDescription);
			context.Print ("");

			if (expression.HelpText != null)
				context.Print (expression.HelpText);
			else
				context.Print ("No help available yet.");
		}

		public ExpressionClass GetExpression (Type type)
		{
			ExpressionClass expression = (ExpressionClass) expression_hash [type];
			if (expression == null)
				throw new GeneratorException ("Unknown expression type: {0}", type);

			return expression;
		}
	}
}
