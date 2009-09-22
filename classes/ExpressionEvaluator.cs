using System;
using System.Text;
using System.Threading;
using System.Diagnostics;
using Mono.Debugger.Languages;
using EE=Mono.Debugger.ExpressionEvaluator;

namespace Mono.Debugger
{
	public class ExpressionParsingException : Exception
	{
		public readonly string Expression;
		public readonly int Position;

		public ExpressionParsingException (string expression, int pos, string message)
			: base (message)
		{
			this.Expression = expression;
			this.Position = pos;
		}

		public override string ToString ()
		{
			return String.Format ("Failed to parse expression `{0}': syntax error at position {1}: {2}",
					      Expression, Position, Message);
		}
	}

	public class EvaluationTimeoutException : Exception
	{ }

	public static class ExpressionEvaluator
	{
		public enum EvaluationResult
		{
			Ok,
			UnknownError,
			MethodNotFound,
			NotInitialized,
			InvalidExpression,
			Exception,
			Timeout
		}

		[Flags]
		public enum EvaluationFlags
		{
			None			= 0,
			NestedBreakStates	= 1
		}

		public interface IExpression
		{
			string Name {
				get;
			}

			AsyncResult Evaluate (StackFrame frame, EE.EvaluationFlags flags,
					      EE.EvaluationCallback callback);

			AsyncResult Assign (StackFrame frame, TargetObject obj,
					    EE.EvaluationCallback callback);
		}

		public abstract class AsyncResult : IAsyncResult
		{
			public abstract object AsyncState {
				get;
			}

			public abstract WaitHandle AsyncWaitHandle {
				get;
			}

			public abstract bool CompletedSynchronously {
				get;
			}

			public abstract bool IsCompleted {
				get;
			}

			public abstract void Abort ();
		}

		public delegate void EvaluationCallback (EvaluationResult result, object data);

		public static EvaluationResult MonoObjectToString (Thread thread, TargetStructObject obj,
								   EvaluationFlags flags, int timeout,
								   out string result)
		{
			result = null;

		again:
			TargetStructType ctype = obj.Type;
			if ((ctype.Name == "System.Object") || (ctype.Name == "System.ValueType"))
				return EvaluationResult.MethodNotFound;

			TargetClass klass = ctype.GetClass (thread);
			if (klass == null)
				return EvaluationResult.NotInitialized;

			TargetMethodInfo[] methods = klass.GetMethods (thread);
			if (methods == null)
				return EvaluationResult.MethodNotFound;

			foreach (TargetMethodInfo minfo in methods) {
				if (minfo.Name != "ToString")
					continue;

				TargetFunctionType ftype = minfo.Type;
				if (ftype.ParameterTypes.Length != 0)
					continue;
				if (ftype.ReturnType != ftype.Language.StringType)
					continue;

				RuntimeInvokeResult rti;
				try {
					RuntimeInvokeFlags rti_flags = RuntimeInvokeFlags.VirtualMethod;

					if ((flags & EvaluationFlags.NestedBreakStates) != 0)
						rti_flags |= RuntimeInvokeFlags.NestedBreakStates;

					rti = thread.RuntimeInvoke (
						ftype, obj, new TargetObject [0], rti_flags);

					if (!rti.CompletedEvent.WaitOne (timeout, false)) {
						rti.Abort ();
						return EvaluationResult.Timeout;
					}

					if ((rti.TargetException != null) &&
					    (rti.TargetException.Type == TargetError.ClassNotInitialized)) {
						result = null;
						return EvaluationResult.NotInitialized;
					}

					if (rti.Result is Exception) {
						result = ((Exception) rti.Result).Message;
						return EvaluationResult.UnknownError;
					}

					if (rti.ExceptionMessage != null) {
						result = rti.ExceptionMessage;
						return EvaluationResult.Exception;
					} else if (rti.ReturnObject == null) {
						rti.Abort ();
						return EvaluationResult.UnknownError;
					}
				} catch (TargetException ex) {
					result = ex.ToString ();
					return EvaluationResult.UnknownError;
				}

				TargetObject retval = (TargetObject) rti.ReturnObject;
				result = (string) ((TargetFundamentalObject) retval).GetObject (thread);
				return EvaluationResult.Ok;
			}

			obj = obj.GetParentObject (thread) as TargetStructObject;
			if (obj != null)
				goto again;

			return EvaluationResult.MethodNotFound;
		}

		public static EvaluationResult GetProperty (Thread thread, TargetPropertyInfo property,
							    TargetStructObject instance, EvaluationFlags flags,
							    int timeout, out string error, out TargetObject result)
		{
			error = null;

			RuntimeInvokeResult rti;
			try {
				RuntimeInvokeFlags rti_flags = RuntimeInvokeFlags.VirtualMethod;

				if ((flags & EvaluationFlags.NestedBreakStates) != 0)
					rti_flags |= RuntimeInvokeFlags.NestedBreakStates;

				rti = thread.RuntimeInvoke (
					property.Getter, instance, new TargetObject [0], rti_flags);

				if (!rti.CompletedEvent.WaitOne (timeout, false)) {
					rti.Abort ();
					result = null;
					return EvaluationResult.Timeout;
				}

				if ((rti.TargetException != null) &&
				    (rti.TargetException.Type == TargetError.ClassNotInitialized)) {
					result = null;
					error = rti.ExceptionMessage;
					return EvaluationResult.NotInitialized;
				}

				if (rti.Result is Exception) {
					result = null;
					error = ((Exception) rti.Result).Message;
					return EvaluationResult.UnknownError;
				}

				result = (TargetObject) rti.ReturnObject;

				if (rti.ExceptionMessage != null) {
					error = rti.ExceptionMessage;
					return EvaluationResult.Exception;
				} else if (rti.ReturnObject == null) {
					rti.Abort ();
					return EvaluationResult.UnknownError;
				}

				return EvaluationResult.Ok;
			} catch (TargetException ex) {
				result = null;
				error = ex.ToString ();
				return EvaluationResult.UnknownError;
			}
		}
	}
}
