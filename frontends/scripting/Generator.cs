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
		string jay;
		string short_description;

		public CommandAttribute (string jay, string short_description)
		{
			this.jay = jay;
			this.short_description = short_description;
		}

		public string Jay {
			get { return jay; }
		}

		public string ShortDescription {
			get { return short_description; }
		}
	}

	[AttributeUsage (AttributeTargets.Class)]
	public class ExpressionAttribute : Attribute
	{ 
		string name;
		string description;

		public ExpressionAttribute (string name, string description)
		{
			this.name = name;
			this.description = description;
		}

		public string Name {
			get { return name; }
		}
	}

	public class GeneratorException : Exception
	{
		public GeneratorException (string format, params object[] args)
			: base (String.Format (format, args))
		{ }
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

		public string TypeName {
			get { return type.Name; }
		}

		public static ExpressionClass Create (Type type)
		{
			if (!type.IsSubclassOf (typeof (Expression)))
				return null;

			Attribute[] attributes = Attribute.GetCustomAttributes (
				type, typeof (ExpressionAttribute), true);
			if (attributes.Length != 1)
				return null;

			Console.WriteLine ("EXPRESSION: {0}", type);

			return new ExpressionClass (type, (ExpressionAttribute) attributes [0]);
		}
	}

	public class CommandClass
	{
		Type type;
		CommandAttribute attribute;
		ConstructorInfo ctor;
		ExpressionClass[] args;

		protected CommandClass (Type type, CommandAttribute attribute, ExpressionClass[] args)
		{
			this.type = type;
			this.attribute = attribute;
			this.args = args;
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
				type, typeof (CommandAttribute), true);
			if (attributes.Length != 1)
				return null;

			Console.WriteLine ("COMMAND: {0}", type);

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

		void GetExpressions (Assembly assembly)
		{
			ArrayList list = new ArrayList ();
			expression_hash = new Hashtable ();

			foreach (Type type in assembly.GetExportedTypes ()) {
				ExpressionClass expression = ExpressionClass.Create (type);
				if (expression != null) {
					list.Add (expression);
					expression_hash.Add (type, expression);
				}
			}

			expressions = new ExpressionClass [list.Count];
			list.CopyTo (expressions, 0);
		}

		void GetCommands (Assembly assembly)
		{
			ArrayList list = new ArrayList ();

			foreach (Type type in assembly.GetExportedTypes ()) {
				CommandClass[] commands = CommandClass.Create (this, type);
				if (commands != null)
					list.AddRange (commands);
			}

			commands = new CommandClass [list.Count];
			list.CopyTo (commands, 0);
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
