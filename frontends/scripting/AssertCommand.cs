using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Frontends.CommandLine
{
	public abstract class AssertCommand : Command
	{
		public abstract string Name {
			get;
		}

		protected abstract bool DoCheck (ScriptingContext context);

		protected override void DoExecute (ScriptingContext context)
		{
			try {
				bool ok = DoCheck (context);
				if (ok) {
					if (context.IsInteractive)
						context.Print ("OK");
					return;
				}

				context.Print ("ASSERTION ({0}) FAILED AT {1}",
					       Name, context.CurrentProcess.CurrentFrame);
			} catch (ScriptingException e) {
				context.Print ("ASSERTION ({0}) FAILED WITH ERROR ({1}) AT {2}",
					       Name, e.Message, context.CurrentProcess.CurrentFrame);
			} catch (Exception e) {
				context.Print ("ASSERTION ({0}) FAILED WITH EXCEPTION ({1}) AT {2}",
					       Name, e.Message, context.CurrentProcess.CurrentFrame);
				context.Print ("FULL EXPCETION TEXT: {0}", e);
			}

			context.ExitCode++;
		}
	}

	public class AssertKindCommand : AssertCommand
	{
		TargetObjectKind kind;
		VariableExpression var_expr;

		public override string Name {
			get { return String.Format ("kind ({0}) == {1}", var_expr.Name, kind); }
		}

		public AssertKindCommand (TargetObjectKind kind, VariableExpression var_expr)
		{
			this.kind = kind;
			this.var_expr = var_expr;
		}

		protected override bool DoCheck (ScriptingContext context)
		{
			ITargetType type = var_expr.ResolveType (context);
			return type.Kind == kind;
		}
	}

	public class AssertAccessibleCommand : AssertCommand
	{
		VariableExpression var_expr;
		bool positive;

		public override string Name {
			get {
				return String.Format ("{1}accessible ({0})", var_expr.Name,
						      positive ? "" : "not ");
			}
		}

		public AssertAccessibleCommand (VariableExpression var_expr, bool positive)
		{
			this.var_expr = var_expr;
			this.positive = positive;
		}

		protected override bool DoCheck (ScriptingContext context)
		{
			try {
				var_expr.ResolveVariable (context);
			} catch (ScriptingException) {
				return !positive;
			}
			return positive;
		}
	}

	public class AssertTypeCommand : AssertCommand
	{
		VariableExpression var_expr;
		string type_name;

		public override string Name {
			get {
				return String.Format ("typeof ({0}) == {1}", var_expr.Name, type_name);
			}
		}

		public AssertTypeCommand (string type_name, VariableExpression var_expr)
		{
			this.type_name = type_name;
			this.var_expr = var_expr;
		}

		protected override bool DoCheck (ScriptingContext context)
		{
			ITargetType type = var_expr.ResolveType (context);
			return type.Name == type_name;
		}
	}

	public class AssertContentsCommand : AssertCommand
	{
		VariableExpression var_expr;
		string contents;

		public override string Name {
			get {
				return String.Format ("contents ({0}) == \"{1}\"", var_expr.Name, contents);
			}
		}

		public AssertContentsCommand (string contents, VariableExpression var_expr)
		{
			this.contents = contents;
			this.var_expr = var_expr;
		}

		protected override bool DoCheck (ScriptingContext context)
		{
			ITargetFundamentalObject obj = var_expr.ResolveVariable (context) as ITargetFundamentalObject;
			if (obj == null)
				throw new ScriptingException ("Variable {0} is not a fundamental type.",
							      var_expr.Name);

			if (!obj.HasObject)
				throw new ScriptingException ("Can't get contents of variable {0}.",
							      var_expr.Name);

			return obj.Object.ToString () == contents;
		}
	}

	public class AssertLineCommand : AssertCommand
	{
		int line;

		public override string Name {
			get {
				return String.Format ("line == {0}", line);
			}
		}

		public AssertLineCommand (int line)
		{
			this.line = line;
		}

		protected override bool DoCheck (ScriptingContext context)
		{
			StackFrame frame = context.CurrentProcess.CurrentFrame.Frame;

			if (frame.SourceAddress == null)
				return false;
			else
				return frame.SourceAddress.Row == line;
		}
	}
}
