using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.CommandLine
{
	public abstract class Command
	{
		protected abstract void DoExecute (ScriptingContext context);

		public void Execute (ScriptingContext context)
		{
			try {
				DoExecute (context);
			} catch (ScriptingException ex) {
				context.Error (ex);
			}
		}
	}

	public abstract class Expression
	{
		protected abstract object DoResolve (ScriptingContext context);

		public object Resolve (ScriptingContext context)
		{
			object retval = DoResolve (context);

			if (retval == null)
				throw new ScriptingException ("Can't resolve command: {0}", this);

			return retval;
		}
	}

	public class PrintCommand : Command
	{
		Expression expression;

		public PrintCommand (Expression expression)
		{
			this.expression = expression;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			object retval = expression.Resolve (context);

			if (retval is long)
				context.Print (String.Format ("0x{0:x}", (long) retval));
			else
				context.Print (retval);
		}
	}

	public class FrameCommand : Command
	{
		int number;
		ProcessExpression process_expr;

		public FrameCommand (ProcessExpression process_expr, int number)
		{
			this.process_expr = process_expr;
			this.number = number;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			process.PrintFrame (number);
		}
	}

	public class DisassembleCommand : Command
	{
		int number;
		ProcessExpression process_expr;

		public DisassembleCommand (ProcessExpression process_expr, int number)
		{
			this.process_expr = process_expr;
			this.number = number;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			process.Disassemble (number);
		}
	}

	public class DisassembleMethodCommand : Command
	{
		int number;
		ProcessExpression process_expr;

		public DisassembleMethodCommand (ProcessExpression process_expr, int number)
		{
			this.process_expr = process_expr;
			this.number = number;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			process.DisassembleMethod (number);
		}
	}

	public class StartCommand : Command
	{
		string[] args;
		int pid;

		public StartCommand (string[] args)
		{
			this.args = args;
			this.pid = -1;
		}

		public StartCommand (string[] args, int pid)
		{
			this.args = args;
			this.pid = pid;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			if (!context.IsSynchronous)
				throw new ScriptingException ("This command cannot be used in the GUI.");
			if (pid != -1)
				throw new ScriptingException ("Attaching is not yet implemented.");
			context.Start (args);
		}
	}

	public class SelectProcessCommand : Command
	{
		ProcessExpression process_expr;

		public SelectProcessCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			context.CurrentProcess = process;
		}
	}

	public class BackgroundProcessCommand : Command
	{
		ProcessExpression process_expr;

		public BackgroundProcessCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Background ();
		}
	}

	public class StopProcessCommand : Command
	{
		ProcessExpression process_expr;

		public StopProcessCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Stop ();
		}
	}

	public class StepCommand : Command
	{
		ProcessExpression process_expr;
		WhichStepCommand which;

		public StepCommand (ProcessExpression process_expr, WhichStepCommand which)
		{
			this.process_expr = process_expr;
			this.which = which;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			process.Step (which);
		}
	}

	public class BacktraceCommand : Command
	{
		ProcessExpression process_expr;

		public BacktraceCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			int current_idx = process.CurrentFrameIndex;
			Backtrace backtrace = process.GetBacktrace ();
			for (int i = 0; i < backtrace.Length; i++) {
				string prefix = i == current_idx ? "(*)" : "   ";
				context.Print ("{0} {1}", prefix, backtrace [i]);
			}
		}
	}

	public class UpCommand : Command
	{
		ProcessExpression process_expr;

		public UpCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			process.CurrentFrameIndex++;
			process.PrintFrame ();
		}
	}

	public class DownCommand : Command
	{
		ProcessExpression process_expr;

		public DownCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			process.CurrentFrameIndex--;
			process.PrintFrame ();
		}
	}

	public class KillProcessCommand : Command
	{
		ProcessExpression process_expr;

		public KillProcessCommand (ProcessExpression process_expr)
		{
			this.process_expr = process_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			process.Kill ();
		}
	}

	public class ShowProcessesCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			if (!context.HasTarget) {
				context.Print ("No target.");
				return;
			}

			int current_id = context.CurrentProcess.ID;
			foreach (ProcessHandle proc in context.Processes) {
				string prefix = proc.ID == current_id ? "(*)" : "   ";
				context.Print ("{0} {1}", prefix, proc);
			}
		}
	}

	public class ShowRegistersCommand : Command
	{
		ProcessExpression process_expr;
		int number;

		public ShowRegistersCommand (ProcessExpression process_expr, int number)
		{
			this.process_expr = process_expr;
			this.number = number;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			string[] names = process.Architecture.RegisterNames;
			int[] indices = process.Architecture.RegisterIndices;

			foreach (Register register in process.GetRegisters (number, indices))
				context.Print ("%{0} = 0x{1:x}", names [register.Index], register.Data);
		}
	}

	public class ShowParametersCommand : Command
	{
		ProcessExpression process_expr;
		int number;

		public ShowParametersCommand (ProcessExpression process_expr, int number)
		{
			this.process_expr = process_expr;
			this.number = number;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			process.ShowParameters (number);
		}
	}

	public class ShowLocalsCommand : Command
	{
		ProcessExpression process_expr;
		int number;

		public ShowLocalsCommand (ProcessExpression process_expr, int number)
		{
			this.process_expr = process_expr;
			this.number = number;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			process.ShowLocals (number);
		}
	}

	public class ShowVariableTypeCommand : Command
	{
		VariableExpression var_expr;

		public ShowVariableTypeCommand (VariableExpression var_expr)
		{
			this.var_expr = var_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ITargetType type = var_expr.ResolveType (context);

			context.ShowVariableType (type, var_expr.Name);
		}
	}

	public class ShowModulesCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.ShowModules ();
		}
	}

	public class ShowSourcesCommand : Command
	{
		int[] modules;

		public ShowSourcesCommand (int[] modules)
		{
			this.modules = modules;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context.ShowSources (modules);
		}
	}

	public class ShowBreakpointsCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.ShowBreakpoints ();
		}
	}

	public class ShowThreadGroupsCommand : Command
	{
		protected override void DoExecute (ScriptingContext context)
		{
			context.ShowThreadGroups ();
		}
	}

	public class ThreadGroupCreateCommand : Command
	{
		string name;
		int[] threads;

		public ThreadGroupCreateCommand (string name, int[] threads)
		{
			this.name = name;
			this.threads = threads;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context.CreateThreadGroup (name, threads);
		}
	}

	public class ThreadGroupAddCommand : Command
	{
		string name;
		int[] threads;

		public ThreadGroupAddCommand (string name, int[] threads)
		{
			this.name = name;
			this.threads = threads;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context.AddToThreadGroup (name, threads);
		}
	}

	public class ThreadGroupRemoveCommand : Command
	{
		string name;
		int[] threads;

		public ThreadGroupRemoveCommand (string name, int[] threads)
		{
			this.name = name;
			this.threads = threads;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context.RemoveFromThreadGroup (name, threads);
		}
	}

	public class BreakpointEnableCommand : Command
	{
		int index;

		public BreakpointEnableCommand (int index)
		{
			this.index = index;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			Breakpoint breakpoint = context.FindBreakpoint (index);
			breakpoint.Enabled = true;
		}
	}

	public class BreakpointDisableCommand : Command
	{
		int index;

		public BreakpointDisableCommand (int index)
		{
			this.index = index;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			Breakpoint breakpoint = context.FindBreakpoint (index);
			breakpoint.Enabled = false;
		}
	}

	public class BreakpointDeleteCommand : Command
	{
		int index;

		public BreakpointDeleteCommand (int index)
		{
			this.index = index;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context.DeleteBreakpoint (index);
		}
	}

	public class ModuleOperationCommand : Command
	{
		int[] modules;
		ModuleOperation[] operations;

		public ModuleOperationCommand (int[] modules, ModuleOperation[] operations)
		{
			this.modules = modules;
			this.operations = operations;
		}

		public ModuleOperationCommand (ModuleOperation[] operations)
		{
			this.operations = operations;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			if (modules != null)
				context.ModuleOperations (modules, operations);
			else
				context.ModuleOperations (operations);
		}
	}

	public class BreakCommand : Command
	{
		ThreadGroupExpression thread_group_expr;
		SourceExpression source_expr;

		public BreakCommand (ThreadGroupExpression thread_group_expr, SourceExpression source_expr)
		{
			this.thread_group_expr = thread_group_expr;
			this.source_expr = source_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			ThreadGroup group = (ThreadGroup) thread_group_expr.Resolve (context);

			SourceMethod method = source_expr.ResolveMethod (context);
			if (method == null)
				return;

			int index = context.InsertBreakpoint (group, new SourceLocation (method));
			context.Print ("Inserted breakpoint {0}.", index);
		}
	}

	public class ScriptingVariableAssignCommand : Command
	{
		string identifier;
		VariableExpression expr;

		public ScriptingVariableAssignCommand (string identifier, VariableExpression expr)
		{
			this.identifier = identifier;
			this.expr = expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context [identifier] = expr;
		}
	}

	public class NumberExpression : Expression
	{
		int val;

		public NumberExpression (int val)
		{
			this.val = val;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return this.val;
		}

		public override string ToString ()
		{
			return String.Format ("{0} {1:x}", GetType(), this.val);
		}
	}

	// So you can extend this by just creating a subclass
	// of BinaryOperator that implements DoEvaluate and
	// a constructor, but you'll need to add a new rule to
	// the parser of the form
	//
	// expression: my_param_kind MY_OP_TOKEN my_param_kind 
	//             { $$ = new MyBinarySubclass ((MyParam) $1, (MyParam) $3); }
	//
	// If you want to extend on of { +, -, *, /} for non-integers,
	// like supporting "a" + "b" = "ab", then larger changes would
	// be needed.

	public class BinaryOperator : Expression
	{
		public enum Kind { Mult, Plus, Minus, Div };

		protected Kind kind;
		protected Expression left, right;

		public BinaryOperator (Kind kind, Expression left, Expression right)
		{
			this.kind = kind;
			this.left = left;
			this.right = right;
		}

		protected object DoEvaluate (ScriptingContext context, object lobj, object robj)
		{
			switch (kind) {
			case Kind.Mult:
				return (int) lobj * (int) robj;
			case Kind.Plus:
				return (int) lobj + (int) robj;
			case Kind.Minus:
				return (int) lobj - (int) robj;
			case Kind.Div:
				return (int) lobj / (int) robj;
			}

			throw new ScriptingException ("Unknown binary operator kind: {0}", kind);
		}

		protected override object DoResolve (ScriptingContext context)
		{
			object lobj, robj;

			lobj = left.Resolve (context);
			robj = right.Resolve (context);

			// Console.WriteLine ("bin eval: {0} ({1}) and {2} ({3})", lobj, lobj.GetType(), robj, robj.GetType());
			return DoEvaluate (context, lobj, robj);
		}
	}

	public class ProcessExpression : Expression
	{
		int number;

		public ProcessExpression (int number)
		{
			this.number = number;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return context.ProcessByID (number);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), number);
		}
	}

	public class ThreadGroupExpression : Expression
	{
		string name;

		public ThreadGroupExpression (string name)
		{
			this.name = name;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ThreadGroup group = ThreadGroup.ThreadGroupByName (name);
			if (group == null)
				throw new ScriptingException ("No such thread group.");

			return group;
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), name);
		}
	}

	public class RegisterExpression : Expression
	{
		int number;
		ProcessExpression process_expr;
		string register;

		public RegisterExpression (ProcessExpression process_expr, int number, string register)
		{
			this.process_expr = process_expr;
			this.number = number;
			this.register = register;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			return process.GetRegister (number, register);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1},{2},{3})", GetType (), process_expr, number,
					      register);
		}
	}

	public abstract class VariableExpression : Expression
	{
		public abstract string Name {
			get;
		}

		protected abstract ITargetObject DoResolveVariable (ScriptingContext context);

		public ITargetObject ResolveVariable (ScriptingContext context)
		{
			ITargetObject retval = DoResolveVariable (context);
			if (retval == null)
				throw new ScriptingException ("Can't resolve variable: {0}", this);

			return retval;
		}

		protected abstract ITargetType DoResolveType (ScriptingContext context);

		public ITargetType ResolveType (ScriptingContext context)
		{
			ITargetType type = DoResolveType (context);
			if (type == null)
				throw new ScriptingException ("Can't get type of variable {0}.", this);

			return type;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ITargetObject obj = ResolveVariable (context);
			ITargetType type = ResolveType (context);

			if (!obj.IsValid)
				throw new ScriptingException ("Variable {0} is out of scope.", this);

			if (type.Kind == TargetObjectKind.Fundamental)
				return ((ITargetFundamentalObject) obj).Object;

			// FIXME: how to handle all the other kinds of objects?
			return obj;
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1})", GetType (), Name);
		}
	}

	public class ExpressionGroup : Expression
	{
		Expression expr;

		public ExpressionGroup (Expression expr)
		{
			this.expr = expr;
		}

		public override string ToString()
		{
			return '(' + expr.ToString() + ')';
		}

		protected override object DoResolve (ScriptingContext context)
		{
			return expr.Resolve (context);
		}
	}

	public class ScriptingVariableReference : VariableExpression
	{
		string identifier;

		public ScriptingVariableReference (string identifier)
		{
			this.identifier = identifier;
		}

		public override string Name {
			get { return '!' + identifier; }
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			VariableExpression expr = context [identifier];
			if (expr == null)
				return null;

			return expr.ResolveVariable (context);
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			VariableExpression expr = context [identifier];
			if (expr == null)
				return null;

			return expr.ResolveType (context);
		}
	}

	public class VariableReferenceExpression : VariableExpression
	{
		int number;
		ProcessExpression process_expr;
		string identifier;

		public VariableReferenceExpression (ProcessExpression process_expr, int number,
						    string identifier)
		{
			this.process_expr = process_expr;
			this.number = number;
			this.identifier = identifier;
		}

		public override string Name {
			get { return '$' + identifier; }
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			return process.GetVariable (number, identifier);
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);

			return process.GetVariableType (number, identifier);
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1},{2},{3})", GetType (), process_expr, number,
					      identifier);
		}
	}

	public class StructAccessExpression : VariableExpression
	{
		VariableExpression var_expr;
		string identifier;

		public StructAccessExpression (VariableExpression var_expr, string identifier)
		{
			this.var_expr = var_expr;
			this.identifier = identifier;
		}

		public override string Name {
			get {
				return String.Format ("{0}.{1}", var_expr.Name, identifier);
			}
		}

		ITargetFieldInfo get_field (ITargetStructType tstruct)
		{
			foreach (ITargetFieldInfo field in tstruct.Fields)
				if (field.Name == identifier)
					return field;

			throw new ScriptingException ("Variable {0} has no field {1}.", var_expr.Name,
						      identifier);
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			ITargetStructObject sobj = var_expr.ResolveVariable (context) as ITargetStructObject;
			if (sobj == null)
				throw new ScriptingException ("Variable {0} is not a struct or class type.",
							      var_expr.Name);

			ITargetFieldInfo field = get_field (sobj.Type);
			return sobj.GetField (field.Index);
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			ITargetStructType tstruct = var_expr.ResolveType (context) as ITargetStructType;
			if (tstruct == null)
				throw new ScriptingException ("Variable {0} is not a struct or class type.",
							      var_expr.Name);

			ITargetFieldInfo field = get_field (tstruct);
			return field.Type;
		}
	}

	public class VariableDereferenceExpression : VariableExpression
	{
		VariableExpression var_expr;

		public VariableDereferenceExpression (VariableExpression var_expr)
		{
			this.var_expr = var_expr;
		}

		public override string Name {
			get {
				return '*' + var_expr.Name;
			}
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			ITargetPointerObject pobj = var_expr.ResolveVariable (context) as ITargetPointerObject;
			if (pobj == null)
				throw new ScriptingException ("Variable {0} is not a pointer type.",
							      var_expr.Name);

			if (!pobj.HasDereferencedObject)
				throw new ScriptingException ("Cannot dereference {0}.", var_expr.Name);

			return pobj.DereferencedObject;
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			ITargetPointerObject pobj = var_expr.ResolveVariable (context) as ITargetPointerObject;
			if (pobj == null)
				throw new ScriptingException ("Variable {0} is not a pointer type.",
							      var_expr.Name);

			ITargetType type = pobj.CurrentType;
			if (type == null)
				throw new ScriptingException ("Cannot get current type of {0}.", var_expr.Name);

			return type;
		}
	}

	public class ArrayAccessExpression : VariableExpression
	{
		VariableExpression var_expr;
		Expression index;

		public ArrayAccessExpression (VariableExpression var_expr, Expression index)
		{
			this.var_expr = var_expr;
			this.index = index;
		}

		public override string Name {
			get {
				return String.Format ("{0}[{1}]", var_expr.Name, index);
			}
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			int i;

			ITargetArrayObject obj = var_expr.ResolveVariable (context) as ITargetArrayObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not an array type.", var_expr.Name);
			try {
				i = (int) this.index.Resolve (context);
			} catch (Exception e) {
				throw new ScriptingException (
					"Cannot convert {0} to an integer for indexing: {1}", this.index, e);
			}

			if ((i < obj.LowerBound) || (i >= obj.UpperBound))
				throw new ScriptingException (
					"Index {0} of array expression {1} out of bounds " +
					"(must be between {2} and {3}).", i, var_expr.Name,
					obj.LowerBound, obj.UpperBound - 1);

			return obj [i];
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			ITargetArrayType type = var_expr.ResolveType (context) as ITargetArrayType;
			if (type == null)
				throw new ScriptingException ("Variable {0} is not an array type.",
							      var_expr.Name);

			return type.ElementType;
		}
	}

	public class ParentClassExpression : VariableExpression
	{
		VariableExpression var_expr;

		public ParentClassExpression (VariableExpression var_expr)
		{
			this.var_expr = var_expr;
		}

		public override string Name {
			get {
				return String.Format ("parent ({0})", var_expr.Name);
			}
		}

		protected override ITargetObject DoResolveVariable (ScriptingContext context)
		{
			ITargetClassObject obj = var_expr.ResolveVariable (context) as ITargetClassObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not a class type.", var_expr.Name);

			if (!obj.Type.HasParent)
				throw new ScriptingException ("Variable {0} doesn't have a parent type.",
							      var_expr.Name);

			return obj.Parent;
		}

		protected override ITargetType DoResolveType (ScriptingContext context)
		{
			ITargetClassType type = var_expr.ResolveType (context) as ITargetClassType;
			if (type == null)
				throw new ScriptingException ("Variable {0} is not a class type.",
							      var_expr.Name);

			if (!type.HasParent)
				throw new ScriptingException ("Variable {0} doesn't have a parent type.",
							      var_expr.Name);

			return type.ParentType;
		}
	}

	public class ArrayLengthExpression : Expression
	{
		VariableExpression var_expr;

		public ArrayLengthExpression (VariableExpression var_expr)
		{
			this.var_expr = var_expr;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ITargetArrayObject obj = var_expr.ResolveVariable (context) as ITargetArrayObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not an array type.", var_expr.Name);

			return obj.UpperBound - obj.LowerBound;
		}
	}

	public class ArrayLowerBoundExpression : Expression
	{
		VariableExpression var_expr;

		public ArrayLowerBoundExpression (VariableExpression var_expr)
		{
			this.var_expr = var_expr;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ITargetArrayObject obj = var_expr.ResolveVariable (context) as ITargetArrayObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not an array type.", var_expr.Name);

			return obj.LowerBound;
		}
	}

	public class ArrayUpperBoundExpression : Expression
	{
		VariableExpression var_expr;

		public ArrayUpperBoundExpression (VariableExpression var_expr)
		{
			this.var_expr = var_expr;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			ITargetArrayObject obj = var_expr.ResolveVariable (context) as ITargetArrayObject;
			if (obj == null)
				throw new ScriptingException (
					"Variable {0} is not an array type.", var_expr.Name);

			return obj.UpperBound;
		}
	}

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
			StackFrame frame = context.CurrentProcess.CurrentFrame;

			if (frame.SourceAddress == null)
				return false;
			else
				return frame.SourceAddress.Row == line;
		}
	}

	public class SaveCommand : Command
	{
		string filename;

		public SaveCommand (string filename)
		{
			this.filename = filename;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			context.Save (filename);
		}
	}

	public class HelpCommand : Command
	{
		string type;
		
		public HelpCommand (string t)
		{
			type = t;
		}
		
		protected override void DoExecute (ScriptingContext context)
		{
			switch (type){
			case "show":
				context.Print (
					"show processes          Processes\n" +
					"show registers [p][f]    CPU register contents for [p]rocess/[f]rame\n"+
					"show parameters [p][f]  Parameters for [p]rocess/[f]rame\n"+
					"show locals [p][f]      Local variables for [p]rocess/[f]rame\n"+
					"show modules            The list of loaded modules\n" +
					"show threadgroups       The list of threadgroups\n"+
					"show type <expr>        displays the type for an expression\n");
				break;
			case "":
				context.Print (
					"    backtrace            prints out the backtrace\n" +
					"    frame [proc][fn]     Selects frame\n" + 
					"    c, continue          continue execution\n" +
					"    s, step              single steps\n" +
					"    stepi                single step, at instruction level\n" + 
					"    n, next              next line\n" +
					"    nexti                next line, at instruction level\n" +
					"    finish               runs until the end of the current method\n" + 
					"    up [N]               \n" +
					"    down [N]             \n" +
					"    kill PID             \n" +
					"    show OPT             Shows some information, use help show for details\n" +
					"    print expr           Prints the value of expression\n" + 
					"    \n" +
					"Breakpoints:\n" +
					"    break [tg][func]     inserts a breakpoint, use help break\n" +
					"    breakpoint           manages the breakoints, use help break\n" +
					"    \n" +	          
					"    print EXPR           Prints the expression\n" +
					"    quit                 quits the debugger");
				break;
			default:
				break;
			}
		}
	}

	public class SourceExpression : Expression
	{
		ProcessExpression process_expr;
		int line, number, history_id;
		string file_name, name;

		public SourceExpression (ProcessExpression process_expr, int number, string name, int line)
		{
			this.process_expr = process_expr;
			this.history_id = -1;
			this.number = number;
			this.name = name;
			this.line = line;
		}

		public SourceExpression (ProcessExpression process_expr, int number, int line)
		{
			this.process_expr = process_expr;
			this.history_id = -1;
			this.number = number;
			this.line = line;
		}

		public SourceExpression (int history_id)
		{
			this.history_id = history_id;
		}

		public SourceExpression (string file_name, int line)
		{
			this.file_name = file_name;
			this.line = line;
		}

		public SourceExpression (string file_name)
		{
			this.file_name = file_name;
			this.line = -1;
		}

		public SourceMethod ResolveMethod (ScriptingContext context)
		{
			object result = Resolve (context);
			if (result == null)
				throw new ScriptingException ("No such method.");

			SourceMethod method = result as SourceMethod;
			if (method == null) {
				context.AddMethodSearchResult ((SourceMethod []) result);
				return null;
			}

			if (line == -1)
				return method;

			if ((line < method.StartRow) || (line > method.EndRow))
				throw new ScriptingException ("Requested line number outside of method.");

			return method;
		}

		protected override object DoResolve (ScriptingContext context)
		{
			if (history_id > 0)
				return context.GetMethodSearchResult (history_id);

			if (file_name != null) {
				if (line == -1)
					return context.FindMethod (file_name);
				else
					return context.FindMethod (file_name, line);
			}

			ProcessHandle process = (ProcessHandle) process_expr.Resolve (context);
			StackFrame frame = process.GetFrame (number);

			IMethod method = frame.Method;
			if ((method == null) || !method.HasSource)
				throw new ScriptingException ("No current method.");

			IMethodSource source = method.Source;
			if (name == null) {
				if (source.SourceBuffer.HasContents)
					throw new ScriptingException ("Current method has no source file.");

				return context.FindMethod (source.SourceBuffer.Name, line);
			}

			SourceMethod[] result = source.MethodLookup (name);

			if (result.Length == 0)
				throw new ScriptingException ("No method matches your query.");
			else if (result.Length == 1)
				return result [0];
			else
				return result;
		}
	}

	public class ListCommand : Command
	{
		SourceExpression source_expr;

		public ListCommand (SourceExpression source_expr)
		{
			this.source_expr = source_expr;
		}

		protected override void DoExecute (ScriptingContext context)
		{
			SourceMethod method = source_expr.ResolveMethod (context);
			if (method == null)
				return;

			context.Print ("Method: {0}", method);
		}
	}
}
