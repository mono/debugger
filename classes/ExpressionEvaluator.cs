using System;
using System.Text;
using System.Diagnostics;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	public static class ExpressionEvaluator
	{
		public enum EvaluationResult
		{
			Ok,
			UnknownError,
			MethodNotFound,
			InvalidExpression,
			Exception,
			Timeout
		}

		public static EvaluationResult MonoObjectToString (Thread thread, TargetStructObject obj,
								   int timeout, out string result)
		{
			result = null;

		again:
			TargetStructType ctype = obj.Type;
			if ((ctype.Name == "System.Object") || (ctype.Name == "System.ValueType"))
				return EvaluationResult.MethodNotFound;

			TargetClass klass = ctype.GetClass (thread);
			if (klass == null)
				return EvaluationResult.MethodNotFound;

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
					rti = thread.RuntimeInvoke (ftype, obj, new TargetObject [0], true, false);

					if (!rti.CompletedEvent.WaitOne (timeout, false)) {
						rti.Abort ();
						rti.CompletedEvent.WaitOne ();
						thread.AbortInvocation ();
						return EvaluationResult.Timeout;
					}

					if (rti.Result is Exception) {
						result = ((Exception) rti.Result).Message;
						return EvaluationResult.UnknownError;
					}

					if (rti.ExceptionMessage != null) {
						result = rti.ExceptionMessage;
						return EvaluationResult.Exception;
					} else if (rti.ReturnObject == null) {
						thread.AbortInvocation ();
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
							    TargetStructObject instance, int timeout,
							    out string error, out TargetObject result)
		{
			error = null;

			RuntimeInvokeResult rti;
			try {
				rti = thread.RuntimeInvoke (
					property.Getter, instance, new TargetObject [0], true, false);

				if (!rti.CompletedEvent.WaitOne (timeout, false)) {
					rti.Abort ();
					rti.CompletedEvent.WaitOne ();
					thread.AbortInvocation ();
					result = null;
					return EvaluationResult.Timeout;
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
					thread.AbortInvocation ();
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
