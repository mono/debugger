using System;
using System.Collections;

namespace Mono.Debugger.Frontend
{
	public class Display {
		string argument;
		public string Argument {
			get {
				return argument;
			}
		}
		
		int index;
		public int Index {
			get {
				return index;
			}
		}
		
		bool show_warnings;
		public bool ShowWarnings {
			get {
				return show_warnings;
			}
			set {
				show_warnings = value;
			}
		}
		
		static int next_display_index = 0;
		public static int GetNextDisplayIndex () {
			next_display_index ++;
			return next_display_index;
		}
		
		public Display (string argument) {
			this.argument = argument;
			index = GetNextDisplayIndex ();
			ShowWarnings = true;
		}
		
		public static ScriptingContext ContextFromInterpreter (Interpreter interpreter) {
			ScriptingContext context = new ScriptingContext (interpreter);
			
			context.CurrentProcess = context.Interpreter.CurrentProcess;
			context.CurrentThread = context.Interpreter.CurrentThread;
			if (!context.CurrentThread.IsStopped)
				throw new TargetException (TargetError.NotStopped);
			Backtrace backtrace = context.CurrentThread.GetBacktrace ();
			context.CurrentFrame = backtrace.CurrentFrame;
			
			return context;
		}
		
		void ShowDisabled (ScriptingContext context) {
			context.Print ("Display {0} \"{1}\" has been disabled", index, Argument);
		}
		void Execute (ScriptingContext context, bool showAll) {
			Expression expression = null;
			try {
				IExpressionParser parser = context.Interpreter.GetExpressionParser (context, "display");
				parser.Verbose = false;
				expression = (Expression) parser.Parse (argument);
			} catch (Exception) {
				expression = null;
			}
			if (expression == null) {
				if (ShowWarnings) {
					context.Print ("Display {0}: cannot parse {1}", index, argument);
					ShowWarnings = false;
				} else if (showAll) {
					ShowDisabled (context);
				}
				return;
			}
			
			try {
				expression = expression.Resolve (context);
			} catch (Exception) {
				expression = null;
			}
			if (expression == null) {
				if (ShowWarnings) {
					context.Print ("Display {0}: cannot resolve {1}", index, argument);
					ShowWarnings = false;
				} else if (showAll) {
					ShowDisabled (context);
				}
				return;
			}
			
			ShowWarnings = true;
			object retval = expression.Evaluate (context);
			string text = context.FormatObject (retval, DisplayFormat.Object);
			context.Print ("Display {0} \"{1}\": {2}", index, Argument, text);
		}
		
		public void Execute (ScriptingContext context) {
			Execute (context, false);
		}
		
		public void Show (ScriptingContext context) {
			Execute (context, true);
		}
	}
	public class DisplayCollection {
		Hashtable displays = new Hashtable ();
		
		public Display CreateDisplay (Interpreter interpreter, string argument) {
			Display display = new Display (argument);
			displays [display.Index] = display;
			return display;
		}
		public bool RemoveDisplay (int index) {
			if (displays.ContainsKey (index)) {
				displays.Remove (index);
				return true;
			} else {
				return false;
			}
		}
		public void Execute (Interpreter interpreter) {
			ScriptingContext context = Display.ContextFromInterpreter (interpreter);
			
			foreach (Display display in displays.Values) {
				display.Execute (context);
			}
		}
		public void Show (Interpreter interpreter) {
			ScriptingContext context = Display.ContextFromInterpreter (interpreter);
			
			foreach (Display display in displays.Values) {
				display.Show (context);
			}
		}
	}
}
