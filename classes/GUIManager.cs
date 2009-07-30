using System;
using System.Linq;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using ST = System.Threading;

using Mono.Debugger.Backend;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger
{
	using EE = ExpressionEvaluator;

	[Obsolete]
	public class GUIManager : DebuggerMarshalByRefObject
	{
		public Debugger Debugger {
			get; private set;
		}

		public ThreadingModel ThreadingModel {
			get; set;
		}

		internal GUIManager (Debugger debugger)
		{
			this.Debugger = debugger;
			this.ThreadingModel = ThreadingModel.Global;

			debugger.ProcessExitedEvent += delegate (Debugger dummy, Process process) {
				try {
					if (ProcessExitedEvent == null)
						return;
					ST.ThreadPool.QueueUserWorkItem (delegate {
						ProcessExitedEvent (debugger, process);
					});
				} catch (Exception ex) {
					Report.Error ("Caught exception while sending process {0} exit:\n{1}",
						      process, ex);
				}
			};

			debugger.TargetEvent += delegate (Thread thread, TargetEventArgs args) {
				try {
					if (TargetEvent == null)
						return;
					ST.ThreadPool.QueueUserWorkItem (delegate {
						TargetEvent (thread, args);
					});
				} catch (Exception ex) {
					Report.Error ("{0} caught exception while sending {1}:\n{2}",
						      thread, args, ex);
				}
			};
		}

		public event TargetEventHandler TargetEvent;
		public event ProcessEventHandler ProcessExitedEvent;

		public void Stop (Thread thread)
		{
			thread.Stop ();
		}

		public void Continue (Thread thread)
		{
			thread.Step (ThreadingModel, StepMode.Run);
		}

		public void StepInto (Thread thread)
		{
			thread.Step (ThreadingModel, StepMode.SourceLine);
		}

		public void StepOver (Thread thread)
		{
			thread.Step (ThreadingModel, StepMode.NextLine);
		}

		public void StepOut (Thread thread)
		{
			thread.Step (ThreadingModel, StepMode.Finish);
		}


		public EE.EvaluationResult MonoObjectToString (Thread thread, TargetStructObject obj,
							       EE.EvaluationFlags flags, int timeout,
							       out string text)
		{
			return EE.MonoObjectToString (thread, obj, flags, timeout, out text);
		}

		public EE.EvaluationResult GetProperty (Thread thread, TargetPropertyInfo property,
							TargetStructObject instance, EE.EvaluationFlags flags,
							int timeout, out string error, out TargetObject value)
		{
			return EE.GetProperty (thread, property, instance, flags, timeout, out error, out value);
		}

		public EE.AsyncResult EvaluateExpressionAsync (StackFrame frame, EE.IExpression expression,
							       EE.EvaluationFlags flags, EE.EvaluationCallback cb)
		{
			return expression.EvaluateAsync (frame, flags, cb);
		}
	}
}
