using System;
using System.Text;
using System.Threading;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Mono.Debugger;
using Mono.Debugger.Languages;
using EE = Mono.Debugger.ExpressionEvaluator;

using Mono.GetOptions;

namespace Mono.Debugger.Frontend
{
	public class ScriptingException : Exception
	{
		public ScriptingException (string format, params object[] args)
			: base (String.Format (format, args))
		{ }
	}

	public class InvocationException : ScriptingException
	{
		public readonly TargetObject Exception;
		public readonly string Message;

		public InvocationException (string expr, string message, TargetObject exc)
			: base ("Invocation of `{0}' raised an exception: {1}", expr, message)
		{
			this.Exception = exc;
			this.Message = message;
		}
	}

	public enum ModuleOperation
	{
		Ignore,
		UnIgnore,
		Step,
		DontStep
	}

	[Flags]
	public enum ScriptingFlags
	{
		None			= 0,
		NestedBreakStates	= 1
	}

	public interface IInterruptionHandler
	{
		WaitHandle InterruptionEvent {
			get;
		}

		bool CheckInterruption ();
	}

	public class ScriptingContext : DebuggerMarshalByRefObject
	{
		Thread current_thread;
		Process current_process;
		Language current_language;
		StackFrame current_frame;
		Interpreter interpreter;

		public ScriptingContext (Interpreter interpreter)
		{
			this.interpreter = interpreter;
		}

		public Interpreter Interpreter {
			get { return interpreter; }
		}

		public ScriptingFlags ScriptingFlags {
			get; set;
		}

		public IInterruptionHandler InterruptionHandler {
			get; set;
		}

		public RuntimeInvokeFlags GetRuntimeInvokeFlags ()
		{
			RuntimeInvokeFlags flags = RuntimeInvokeFlags.VirtualMethod;

			if ((ScriptingFlags & ScriptingFlags.NestedBreakStates) != 0)
				flags |= RuntimeInvokeFlags.NestedBreakStates;

			return flags;
		}

		public bool HasTarget {
			get {
				return interpreter.HasTarget;
			}
		}

		public Process CurrentProcess {
			get {
				if (current_process == null)
					throw new TargetException (TargetError.NoTarget);

				return current_process;
			}

			set {
				current_process = value;
			}
		}

		public Thread CurrentThread {
			get {
				if (current_thread == null)
					throw new TargetException (TargetError.NoTarget);

				return current_thread;
			}

			set {
				current_thread = value;

				if (value != null)
					CurrentProcess = value.Process;
			}
		}

		public bool HasFrame {
			get { return current_frame != null; }
		}

		public StackFrame CurrentFrame {
			get {
				if (current_frame == null)
					throw new TargetException (TargetError.NoTarget);

				return current_frame;
			}

			set {
				current_frame = value;

				if (value != null) {
					CurrentThread = value.Thread;
					CurrentLanguage = value.Language;
				} else {
					CurrentThread = null;
					CurrentLanguage = null;
				}
			}
		}

		public StackFrame GetFrame (int number)
		{
			Thread thread = CurrentThread;
			if (!thread.IsStopped)
				throw new TargetException (TargetError.NotStopped);

			if (number == -1)
				return thread.CurrentFrame;

			Backtrace bt = thread.GetBacktrace ();
			if (number >= bt.Count)
				throw new ScriptingException ("No such frame: {0}", number);

			return bt [number];
		}

		public Language CurrentLanguage {
			get {
				if (current_language == null)
					throw new ScriptingException (
						"Stack frame has no source language.");

				return current_language;
			}

			protected set {
				current_language = value;
			}
		}

		public string[] GetNamespaces ()
		{
			if (HasFrame) {
				Method method = CurrentFrame.Method;
				if ((method == null) || !method.HasLineNumbers)
					return null;

				return method.GetNamespaces ();
			}

			if (ImplicitInstance != null) {
				string full_name = ImplicitInstance.Type.Name;

				List<string> list = new List<string> ();

				int pos;
				int start = full_name.Length - 1;
				while ((pos = full_name.LastIndexOf ('.', start)) > 0) {
					list.Add (full_name.Substring (0, pos));
					start = pos - 1;
				}

				return list.ToArray ();
			}

			return null;
		}

		public SourceLocation CurrentLocation {
			get {
				StackFrame frame = CurrentFrame;
				if (frame.SourceLocation == null)
					throw new ScriptingException (
						"Current location doesn't have source code");

				return frame.SourceLocation;
			}
		}

		public AddressDomain AddressDomain {
			get {
				return CurrentThread.AddressDomain;
			}
		}

		public TargetStructObject ImplicitInstance {
			get; private set;
		}

		public void Print (string message)
		{
			interpreter.Print (message);
		}

		public void Print (string format, params object[] args)
		{
			interpreter.Print (String.Format (format, args));
		}

		public void Print (object obj)
		{
			interpreter.Print (obj);
		}

		public Expression ParseExpression (string text)
		{
			try {
				Expression expr = Interpreter.ExpressionParser.ParseInternal (text);
				if (expr == null)
					throw new ScriptingException ("Cannot parse expression `{0}'", text);

				return expr;
			} catch (ExpressionParsingException ex) {
				throw new ScriptingException (ex.ToString ());
			}
		}

		public RuntimeInvokeResult RuntimeInvoke (Thread thread,
							  TargetFunctionType function,
							  TargetStructObject object_argument,
							  TargetObject[] param_objects,
							  RuntimeInvokeFlags flags)
		{
			IInterruptionHandler interruption = InterruptionHandler ?? Interpreter;
			if (interruption.CheckInterruption ())
				throw new EvaluationTimeoutException ();

			RuntimeInvokeResult result = thread.RuntimeInvoke (
				function, object_argument, param_objects, flags);

			WaitHandle[] handles = new WaitHandle [2];
			handles [0] = interruption.InterruptionEvent;
			handles [1] = result.CompletedEvent;

			int ret = WaitHandle.WaitAny (handles);

			if (ret == 0) {
				result.Abort ();
				throw new EvaluationTimeoutException ();
			}

			return result;
		}

		EE.EvaluationResult HandleDebuggerDisplay (Thread thread, TargetStructObject instance,
							   string attr_value, int timeout,
							   out string result)
		{
			result = null;

			StringBuilder sb = new StringBuilder ();

			int pos = 0;

			while (pos < attr_value.Length) {
				if (attr_value [pos] == '\\') {
					if (pos == attr_value.Length)
						break;
					else {
						sb.Append (attr_value [++pos]);
						pos++;
						continue;
					}
				}

				if (attr_value [pos] == '}') {
					result = null;
					return EE.EvaluationResult.InvalidExpression;
				}

				if (attr_value [pos] != '{') {
					sb.Append (attr_value [pos++]);
					continue;
				}

				pos++;
				StringBuilder expr_text = new StringBuilder ();

				while (pos < attr_value.Length) {
					if (attr_value [pos] == '\\') {
						if (pos == attr_value.Length)
							break;
						else {
							expr_text.Append (attr_value [++pos]);
							pos++;
							continue;
						}
					} else if (attr_value [pos] == '{') {
						result = null;
						return EE.EvaluationResult.InvalidExpression;
					} else if (attr_value [pos] == '}') {
						pos++;
						break;
					}

					expr_text.Append (attr_value [pos++]);
				}

				Expression expr;

				try {
					expr = Interpreter.ExpressionParser.ParseInternal (expr_text.ToString ());
				} catch (ExpressionParsingException ex) {
					result = ex.Message;
					return EE.EvaluationResult.InvalidExpression;
				} catch {
					return EE.EvaluationResult.InvalidExpression;
				}

				try {
					expr = expr.Resolve (this);
				} catch (ScriptingException ex) {
					result = ex.Message;
					return EE.EvaluationResult.InvalidExpression;
				} catch {
					return EE.EvaluationResult.InvalidExpression;
				}

				string text;

				try {
					object retval = expr.Evaluate (this);
					if (retval is TargetObject)
						text = DoFormatObject (
							(TargetObject) retval, DisplayFormat.Object);
					else
						text = interpreter.Style.FormatObject (
							CurrentThread, retval, DisplayFormat.Object);
				} catch (ScriptingException ex) {
					result = ex.Message;
					return EE.EvaluationResult.InvalidExpression;
				} catch {
					return EE.EvaluationResult.InvalidExpression;
				}

				sb.Append (text);
			}

			result = sb.ToString ();
			return EE.EvaluationResult.Ok;
		}

		public static EE.EvaluationResult HandleDebuggerDisplay (Interpreter interpreter,
									 Thread thread,
									 TargetStructObject instance,
									 DebuggerDisplayAttribute attr,
									 int timeout, out string name,
									 out string type)
		{
			ScriptingContext expr_context = new ScriptingContext (interpreter);
			expr_context.CurrentThread = thread;
			expr_context.CurrentLanguage = instance.Type.Language;
			expr_context.ImplicitInstance = instance;

			EE.EvaluationResult result = expr_context.HandleDebuggerDisplay (
				 thread, instance, attr.Value, timeout, out name);

			if (result != EE.EvaluationResult.Ok) {
				type = null;
				return result;
			}

			if (String.IsNullOrEmpty (attr.Type)) {
				type = null;
				return EE.EvaluationResult.Ok;
			}

			return expr_context.HandleDebuggerDisplay (
				thread, instance, attr.Type, timeout, out type);
		}

		string MonoObjectToString (TargetClassObject obj)
		{
			TargetClassType ctype = obj.Type;
			if ((ctype.Name == "System.Object") || (ctype.Name == "System.ValueType"))
				return null;

			string text, dummy;
			EE.EvaluationResult result;

			if (ctype.DebuggerDisplayAttribute != null) {
				result = HandleDebuggerDisplay (Interpreter, CurrentThread, obj,
								ctype.DebuggerDisplayAttribute,
								-1, out text, out dummy);
				if (result == EE.EvaluationResult.Ok)
					return String.Format ("{{ {0} }}", text);
				else if (result == EE.EvaluationResult.InvalidExpression) {
					if (text != null)
						return text;
				}
			}

			result = EE.MonoObjectToString (CurrentThread, obj, EE.EvaluationFlags.None, -1, out text);
			if (result == EE.EvaluationResult.Ok)
				return String.Format ("{{ \"{0}\" }}", text);
			return null;
		}

		TargetClassObject CheckTypeProxy (TargetStructObject obj)
		{
			if (obj.Type.DebuggerTypeProxyAttribute == null)
				return null;

			string proxy_name = obj.Type.DebuggerTypeProxyAttribute.ProxyTypeName;
			string original_name = proxy_name;
			proxy_name = proxy_name.Replace ('+', '/');

			Expression expression;
			try {
				expression = new TypeProxyExpression (proxy_name, obj);
				expression = expression.Resolve (this);

				if (expression == null)
					return null;

				return (TargetClassObject) expression.EvaluateObject (this);
			} catch {
				return null;
			}
		}

		public static TargetClassObject CheckTypeProxy (Interpreter interpreter, Thread thread,
								TargetStructObject obj)
		{
			if (obj.Type.DebuggerTypeProxyAttribute == null)
				return null;

			ScriptingContext expr_context = new ScriptingContext (interpreter);
			expr_context.CurrentThread = thread;
			expr_context.CurrentLanguage = obj.Type.Language;
			expr_context.ImplicitInstance = obj;

			return expr_context.CheckTypeProxy (obj);
		}

		string DoFormatObject (TargetObject obj, DisplayFormat format)
		{
			if (format == DisplayFormat.Object) {
				TargetClassObject cobj = obj as TargetClassObject;
				if (cobj != null) {
					string formatted = MonoObjectToString (cobj);
					if (formatted != null)
						return formatted;

					TargetObject proxy = CheckTypeProxy (cobj);
					if (proxy != null)
						obj = proxy;
				}
			}

			return CurrentThread.PrintObject (interpreter.Style, obj, format);
		}

		public string FormatObject (object obj, DisplayFormat format)
		{
			string formatted;
			try {
				if (obj is TargetObject) {
					TargetObject tobj = (TargetObject) obj;
					formatted = String.Format ("({0}) {1}", tobj.TypeName,
								   DoFormatObject (tobj, format));
				} else
					formatted = interpreter.Style.FormatObject (
						CurrentThread, obj, format);
			} catch {
				formatted = "<cannot display object>";
			}
			return formatted;
		}

		public string FormatType (TargetType type)
		{
			string formatted;
			try {
				formatted = CurrentThread.PrintType (
					interpreter.Style, type);
			} catch {
				formatted = "<cannot display type>";
			}
			return (formatted);
		}

		public void PrintMethods (MethodSource[] methods)
		{
			for (int i = 0; i < methods.Length; i++) {
				interpreter.Print ("{0,4}  {1}", i+1, methods [i].Name);
			}
		}

		public void PrintMethods (SourceFile source)
		{
			Print ("Methods from source file {0}: {1}", source.ID, source.FileName);
			PrintMethods (source.Methods);
		}

		public void Dump (object obj)
		{
			if (obj == null)
				Print ("null");
			else if (obj is TargetObject)
				Print (DumpObject ((TargetObject) obj));
			else
				Print ("unknown:{0}:{1}", obj.GetType (), obj);
		}

		public string DumpObject (TargetObject obj)
		{
			return String.Format ("object:{0}", DumpType (obj.Type));
		}

		public string DumpType (TargetType type)
		{
			StringBuilder sb = new StringBuilder ();
			sb.Append (type.Name);
			sb.Append (":");
			sb.Append (type.HasFixedSize);
			sb.Append (":");
			sb.Append (type.Size);
			sb.Append (":");
			sb.Append (type.Kind);
			sb.Append (" ");

			switch (type.Kind) {
			case TargetObjectKind.Fundamental:
				sb.Append (((TargetFundamentalType) type).FundamentalKind);
				break;

			case TargetObjectKind.Pointer: {
				TargetPointerType ptype = (TargetPointerType) type;
				sb.Append (ptype.IsTypesafe);
				sb.Append (":");
				sb.Append (ptype.HasStaticType);
				if (ptype.HasStaticType) {
					sb.Append (":");
					sb.Append (ptype.StaticType.Name);
				}
				break;
			}

			case TargetObjectKind.Array:
				sb.Append (((TargetArrayType) type).ElementType.Name);
				break;

#if FIXME
			case TargetObjectKind.Alias: {
				TargetTypeAlias alias = (TargetTypeAlias) type;
				sb.Append (alias.TargetName);
				if (alias.TargetType != null) {
					sb.Append (":");
					sb.Append (alias.TargetType.Name);
				}
				break;
			}
#endif

			}

			return sb.ToString ();
		}

		public SourceLocation FindMethod (string name)
		{
			foreach (Module module in CurrentProcess.Modules) {
				MethodSource method = module.FindMethod (name);
				
				if (method != null)
					return new SourceLocation (method);
			}

			return null;
		}

		public void ShowSources (Module module)
		{
			if (!module.SymbolsLoaded) {
				Print ("Symbols from module {0} not loaded.", module.Name);
				return;
			}

			Print ("Sources for module {0}:", module.Name);

			foreach (SourceFile source in module.Sources) {
				if (source.IsAutoGenerated &&
				    interpreter.DebuggerConfiguration.HideAutoGenerated)
					continue;
				Print ("{0,4}  {1}{2}", source.ID, source.FileName,
				       source.IsAutoGenerated ? " (auto-generated)" : "");
				if (source.CheckModified ()) {
					Print ("      Source file {0} is more recent than executable.",
					       source.FileName);
				}
			}
		}

		public void LoadLibrary (Thread thread, string filename)
		{
			string pathname = Path.GetFullPath (filename);
			if (!File.Exists (pathname))
				throw new ScriptingException (
					"No such file: `{0}'", pathname);

			try {
				CurrentProcess.LoadLibrary (thread, pathname);
			} catch (TargetException ex) {
				throw new ScriptingException (
					"Cannot load library `{0}': {1}",
					pathname, ex.Message);
			}

			Print ("Loaded library {0}.", filename);
		}

		public void ShowDisplay (Display display)
		{
			if (!HasTarget) {
				Print ("Display {0}: {1}", display.Index, display.Text);
				return;
			}

			try {
				string text = Interpreter.ExpressionParser.EvaluateExpression (
					this, display.Text, DisplayFormat.Object);
				Print ("Display {0} (\"{1}\"): {2}", display.Index, display.Text, text);
			} catch (ScriptingException ex) {
				Print ("Display {0} (\"{1}\"): {2}", display.Index, display.Text,
				       ex.Message);
			} catch (Exception ex) {
				Print ("Display {0} (\"{1}\"): {2}", display.Index, display.Text,
				       ex);
			}
		}
	}
}
