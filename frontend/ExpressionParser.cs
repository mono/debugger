using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using ST=System.Threading;
using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;
using EE=Mono.Debugger.ExpressionEvaluator;

namespace Mono.Debugger.Frontend
{
	public class ExpressionParser : IExpressionParser
	{
		public DebuggerSession Session {
			get; internal set;
		}

		public Interpreter Interpreter {
			get; private set;
		}

		private readonly CSharp.ExpressionParser parser;

		internal ExpressionParser (Interpreter interpreter)
		{
			this.Interpreter = interpreter;

			parser = new CSharp.ExpressionParser ("C#");
		}

		public IExpressionParser Clone (DebuggerSession session)
		{
			ExpressionParser parser = new ExpressionParser (Interpreter);
			parser.Session = session;
			return parser;
		}

		/*
		 * This version throws an `ExpressionParsingException' containing a detailed
		 * error message and the location of the error.
		 *
		 * Use ScriptingContext.ParseExpression() to get a `ScriptingException' with
		 * a user-readable error message.
		 */
		internal Expression ParseInternal (string text)
		{
			return parser.Parse (text);
		}

		public EE.IExpression Parse (string text)
		{
			return new MyExpressionWrapper (this, parser.Parse (text));
		}

		protected SourceLocation FindFile (ScriptingContext context, string filename,
						   int line)
		{
			SourceFile file = Session.FindFile (filename);
			if (file == null)
				throw new ScriptingException ("Cannot find source file `{0}'.",
							      filename);

			MethodSource source = file.FindMethod (line);
			if (source == null)
				throw new ScriptingException (
					"Cannot find method corresponding to line {0} in `{1}'.",
					line, file.Name);

			return new SourceLocation (source, file, line);
		}

		protected SourceLocation DoParseExpression (ScriptingContext context,
							    LocationType type, string arg)
		{
			Expression expr = context.ParseExpression (arg);
			MethodExpression mexpr = expr.ResolveMethod (context, type);

			if (mexpr != null)
				return mexpr.EvaluateSource (context);
			else
				return context.FindMethod (arg);
		}

		public bool ParseLocation (ScriptingContext context, string arg,
					   out SourceLocation location)
		{
			int line;
			int pos = arg.IndexOf (':');
			if (pos >= 0) {
				string filename = arg.Substring (0, pos);
				try {
					line = (int) UInt32.Parse (arg.Substring (pos+1));
				} catch {
					throw new ScriptingException ("Expected filename:line");
				}

				location = FindFile (context, filename, line);
				return true;
			}

			try {
				line = (int) UInt32.Parse (arg);
			} catch {
				location = null;
				return false;
			}

			StackFrame frame = context.CurrentFrame;
			if ((frame == null) || (frame.SourceLocation == null) ||
			    (frame.SourceLocation.FileName == null))
				throw new ScriptingException (
					"Current stack frame doesn't have source code");

			location = FindFile (context, frame.SourceLocation.FileName, line);
			return true;
		}

		protected SourceLocation DoParse (ScriptingContext context, LocationType type,
						  string arg)
		{
			if (type != LocationType.Default)
				return DoParseExpression (context, type, arg);

			SourceLocation location;
			if (ParseLocation (context, arg, out location))
				return location;

			return DoParseExpression (context, type, arg);
		}

		public SourceLocation ParseLocation (Thread target, StackFrame frame,
						     LocationType type, string arg)
		{
			ScriptingContext context = new ScriptingContext (Interpreter);
			context.CurrentThread = target;
			context.CurrentFrame = frame;

			try {
				return DoParse (context, type, arg);
			} catch (ScriptingException ex) {
				throw new TargetException (TargetError.LocationInvalid, ex.Message);
			}
		}

		public string EvaluateExpression (ScriptingContext context, string text,
						  DisplayFormat format)
		{
			Expression expression = context.ParseExpression (text);

			try {
				expression = expression.Resolve (context);
			} catch (ScriptingException ex) {
				throw new ScriptingException ("Cannot resolve expression `{0}': {1}",
							      text, ex.Message);
			} catch {
				throw new ScriptingException ("Cannot resolve expression `{0}'.", text);
			}

			try {
				object retval = expression.Evaluate (context);
				return context.FormatObject (retval, format);
			} catch (ScriptingException ex) {
				throw new ScriptingException ("Cannot evaluate expression `{0}': {1}",
							      text, ex.Message);
			} catch {
				throw new ScriptingException ("Cannot evaluate expression `{0}'.", text);
			}
		}

		public EE.IExpression GetVariableAccessExpression (TargetVariable var)
		{
			return new MyVariableAccessExpression (this, var);
		}

		public EE.IExpression GetMemberAccessExpression (TargetStructType type,
								 TargetStructObject instance,
								 TargetMemberInfo member)
		{
			var sae = new StructAccessExpression (type, instance, member);
			return new MyExpressionWrapper (this, sae);
		}

		protected abstract class MyExpression : EE.IExpression
		{
			public readonly ExpressionParser Parser;

			public abstract string Name {
				get;
			}

			public MyExpression (ExpressionParser parser)
			{
				this.Parser = parser;
			}

			public EE.AsyncResult Evaluate (StackFrame frame, EE.EvaluationFlags flags,
							EE.EvaluationCallback callback)
			{
				MyAsyncResult async = new MyAsyncResult (this);

				ST.ThreadPool.QueueUserWorkItem (delegate {
					ScriptingContext context = new ScriptingContext (Parser.Interpreter);
					context.InterruptionHandler = async;
					context.CurrentFrame = frame;

					if ((flags & EE.EvaluationFlags.NestedBreakStates) != 0)
						context.ScriptingFlags |= ScriptingFlags.NestedBreakStates;

					object data;
					EE.EvaluationResult result = DoEvaluateWorker (context, out data);
					callback (result, data);
					async.WaitHandle.Set ();
				});

				return async;
			}

			protected EE.EvaluationResult DoEvaluateWorker (ScriptingContext context,
									out object result)
			{
				Expression resolved = null;

				try {
					result = DoEvaluate (context);
					return EE.EvaluationResult.Ok;
				} catch (InvocationException ex) {
					result = ex.Exception;
					return EE.EvaluationResult.Exception;
				} catch (ScriptingException ex) {
					result = ex.Message;
					return EE.EvaluationResult.InvalidExpression;
				} catch (EvaluationTimeoutException) {
					result = null;
					return EE.EvaluationResult.Timeout;
				} catch (Exception ex) {
					result = String.Format (
						"Cannot resolve expression `{0}': {1}", Name, ex);
					return EE.EvaluationResult.InvalidExpression;
				}
			}

			protected abstract object DoEvaluate (ScriptingContext context);

			public EE.AsyncResult Assign (StackFrame frame, TargetObject obj,
						      EE.EvaluationCallback callback)
			{
				MyAsyncResult async = new MyAsyncResult (this);

				ST.ThreadPool.QueueUserWorkItem (delegate {
					ScriptingContext context = new ScriptingContext (Parser.Interpreter);
					context.InterruptionHandler = async;
					context.CurrentFrame = frame;

					object data;
					EE.EvaluationResult result = DoAssignWorker (
						context, obj, out data);
					callback (result, data);
					async.WaitHandle.Set ();
				});

				return async;
			}

			protected EE.EvaluationResult DoAssignWorker (ScriptingContext context,
								      TargetObject obj, out object result)
			{
				Expression resolved = null;

				try {
					if (!DoAssign (context, obj)) {
						result = String.Format (
							"Expression `{0}' is not an lvalue", Name);
						return EE.EvaluationResult.InvalidExpression;
					}

					result = null;
					return EE.EvaluationResult.Ok;
				} catch (InvocationException ex) {
					result = ex.Exception;
					return EE.EvaluationResult.Exception;
				} catch (ScriptingException ex) {
					result = ex.Message;
					return EE.EvaluationResult.InvalidExpression;
				} catch (EvaluationTimeoutException) {
					result = null;
					return EE.EvaluationResult.Timeout;
				} catch (Exception ex) {
					result = String.Format (
						"Cannot resolve expression `{0}': {1}", Name, ex);
					return EE.EvaluationResult.InvalidExpression;
				}
			}

			protected abstract bool DoAssign (ScriptingContext context, TargetObject obj);

			public override string ToString ()
			{
				return Name;
			}
		}

		protected class MyExpressionWrapper : MyExpression
		{
			public readonly Expression Expression;

			public override string Name {
				get { return Expression.Name; }
			}

			public MyExpressionWrapper (ExpressionParser parser, Expression expr)
				: base (parser)
			{
				this.Expression = expr;
			}

			protected override object DoEvaluate (ScriptingContext context)
			{
				Expression resolved = Expression.Resolve (context);
				if (resolved == null)
					throw new ScriptingException (
						"Cannot resolve expression `{0}'", Name);

				return resolved.Evaluate (context);
			}

			protected override bool DoAssign (ScriptingContext context, TargetObject obj)
			{
				Expression resolved = Expression.Resolve (context);
				if (resolved == null)
					throw new ScriptingException (
						"Cannot resolve expression `{0}'", Name);

				resolved.Assign (context, obj);
				return true;
			}

			public override string ToString ()
			{
				return Expression.Name;
			}
		}

		protected class MyAsyncResult : EE.AsyncResult, IInterruptionHandler
		{
			public readonly MyExpression Expression;
			public readonly ST.ManualResetEvent WaitHandle;
			public readonly ST.ManualResetEvent AbortHandle;

			public MyAsyncResult (MyExpression expr)
			{
				this.Expression = expr;
				this.WaitHandle = new ST.ManualResetEvent (false);
				this.AbortHandle = new ST.ManualResetEvent (false);
			}

			public override object AsyncState {
				get { return Expression; }
			}

			public override ST.WaitHandle AsyncWaitHandle {
				get { return WaitHandle; }
			}

			public override bool CompletedSynchronously {
				get { return false; }
			}

			public override bool IsCompleted {
				get { return WaitHandle.WaitOne (0); }
			}

			public override void Abort ()
			{
				AbortHandle.Set ();
			}

			ST.WaitHandle IInterruptionHandler.InterruptionEvent {
				get { return AbortHandle; }
			}

			bool IInterruptionHandler.CheckInterruption ()
			{
				return AbortHandle.WaitOne (0);
			}
		}

		protected class MyVariableAccessExpression : MyExpression
		{
			public TargetVariable Variable {
				get; private set;
			}

			public MyVariableAccessExpression (ExpressionParser parser, TargetVariable var)
				: base (parser)
			{
				this.Variable = var;
			}

			public override string Name {
				get { return Variable.Name; }
			}

			protected override object DoEvaluate (ScriptingContext context)
			{
				return Variable.GetObject (context.CurrentFrame);
			}

			protected override bool DoAssign (ScriptingContext context, TargetObject obj)
			{
				if (!Variable.CanWrite)
					return false;

				TargetObject new_obj = Convert.ImplicitConversionRequired (
					context, obj, Variable.Type);

				Variable.SetObject (context.CurrentFrame, new_obj);
				return true;
			}
		}
	}
}
