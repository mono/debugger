using System;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Globalization;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.CommandLine
{
	/// <summary>
	///   This is a very simple command-line interpreter for the Mono Debugger.
	/// </summary>
	public class Interpreter
	{
		DebuggerBackend backend;
		TextWriter stdout, stderr;
		string last_command;
		string[] last_args;

		// <summary>
		//   Create a new command interpreter for the debugger backend @backend.
		//   The interpreter sends its stdout to @stdout and its stderr to @stderr.
		// </summary>
		public Interpreter (DebuggerBackend backend, TextWriter stdout, TextWriter stderr)
		{
			this.backend = backend;
			this.stdout = stdout;
			this.stderr = stderr;
		}

		public void ShowHelp ()
		{
			stderr.WriteLine ("Commands:");
			stderr.WriteLine ("  q, quit,exit     Quit the debugger");
			stderr.WriteLine ("  r, run           Start/continue the target");
			stderr.WriteLine ("  abort            Abort the target");
			stderr.WriteLine ("  kill             Kill the target");
			stderr.WriteLine ("  b, break-method  Add breakpoint for a CSharp method");
			stderr.WriteLine ("  f, frame         Get current stack frame");
			stderr.WriteLine ("  s, step          Single-step");
			stderr.WriteLine ("  n, next          Single-step");
			stderr.WriteLine ("  !, gdb           Send command to gdb");
		}

		// <summary>
		//   Process one command and return true if we should continue processing
		//   commands, ie. until the "quit" command has been issued.
		// </summary>
		public bool ProcessCommand (string line)
		{
			if (line == "") {
				if (last_command == null)
					return true;

				try {
					return ProcessCommand (last_command, last_args);
				} catch (TargetException e) {
					Console.WriteLine (e);
					stderr.WriteLine (e);
					return true;
				}
			}

			string[] tmp_args = line.Split (' ', '\t');
			string[] args = new string [tmp_args.Length - 1];
			Array.Copy (tmp_args, 1, args, 0, tmp_args.Length - 1);
			string command = tmp_args [0];

			last_command = null;
			last_args = new string [0];

			try {
				return ProcessCommand (tmp_args [0], args);
			} catch (Exception e) {
				Console.WriteLine (e);
				stderr.WriteLine (e);
				return true;
			}
		}

		// <summary>
		//   Process one command and return true if we should continue processing
		//   commands, ie. until the "quit" command has been issued.
		// </summary>
		public bool ProcessCommand (string command, string[] args)
		{
			switch (command) {
			case "h":
			case "help":
				ShowHelp ();
				break;

			case "q":
			case "quit":
			case "exit":
				return false;

			case "r":
			case "run":
				backend.Run ();
				break;

			case "c":
			case "continue":
				backend.Continue ();
				last_command = command;
				break;

			case "i":
			case "stepi":
				backend.StepInstruction ();
				last_command = command;
				break;

			case "t":
			case "nexti":
				backend.NextInstruction ();
				last_command = command;
				break;

			case "s":
			case "step":
				backend.StepLine ();
				last_command = command;
				break;

			case "n":
			case "next":
				backend.NextLine ();
				last_command = command;
				break;

			case "finish":
				backend.Finish ();
				break;

			case "stop":
				backend.Stop ();
				break;

			case "sleep":
				Thread.Sleep (50000);
				break;

			case "core": {
				if (args.Length != 1) {
					stderr.WriteLine ("Command requires an argument");
					break;
				}
				backend.ReadCoreFile (args [0]);
				break;
			}

			case "frame":
				Console.WriteLine ("CURRENT FRAME: {0}", backend.CurrentFrameAddress);
				break;

			case "bt":
				backend.GetBacktrace ();
				break;

			case "params": {
				IVariable[] vars = backend.CurrentMethod.Parameters;
				foreach (IVariable var in vars) {
					Console.WriteLine ("PARAM: {0}", var);

					print_type (var.Type);

					if (!var.Type.HasObject)
						continue;

					try {
						ITargetObject obj = var.GetObject (backend.CurrentFrame);
						print_object (obj);
					} catch (LocationInvalidException) {
						// Do nothing.
					}
				}
				break;
			}

			case "locals": {
				IVariable[] vars = backend.CurrentMethod.Locals;
				foreach (IVariable var in vars) {
					Console.WriteLine ("LOCAL: {0}", var);

					print_type (var.Type);

					if (!var.Type.HasObject)
						continue;

					try {
						ITargetObject obj = var.GetObject (backend.CurrentFrame);
						print_object (obj);
					} catch (LocationInvalidException) {
						// Do nothing
					}
				}
				break;
			}

			case "test-break":
				if (args.Length != 1) {
					stderr.WriteLine ("Command requires an argument");
					break;
				}
				backend.TestBreakpoint (args [0]);
				break;

#if FALSE
			case "b":
			case "break-method": {
				if (args.Length != 2) {
					stderr.WriteLine ("Command requires an argument");
					break;
				}

				ILanguageCSharp csharp = backend as ILanguageCSharp;
				if (csharp == null) {
					stderr.WriteLine ("Debugger doesn't support C#");
					break;
				}

				Type type = csharp.CurrentAssembly.GetType (args [0]);
				if (type == null) {
					stderr.WriteLine ("No such type: `" + args [0] + "'");
					break;
				}

				MethodInfo method = type.GetMethod (args [1]);
				if (method == null) {
					stderr.WriteLine ("Can't find method `" + args [1] + "' in type `" +
							  args [0] + "'");
					break;
				}

				ITargetLocation location = csharp.CreateLocation (method);
				if (location == null) {
					stderr.WriteLine ("Can't get location for method: " +
							  args [0] + "." + args [1]);
					break;
				}

				IBreakPoint break_point = backend.AddBreakPoint (location);

				if (break_point != null)
					stderr.WriteLine ("Added breakpoint: " + break_point);
				else
					stderr.WriteLine ("Unable to add breakpoint!");

				break;
			}
#endif

			default:
				stderr.WriteLine ("Unknown command: " + command);
				break;
			}

			return true;
		}

		void print_array (ITargetArrayObject array, int dimension)
		{
			Console.WriteLine ("ARRAY DIMENSION {0}", dimension);
			Console.WriteLine ("DYNAMIC CONTENTS: [{0}]",
					   TargetBinaryReader.HexDump (array.GetRawDynamicContents (-1)));
			
			for (int i = array.LowerBound; i < array.UpperBound; i++) {
				Console.WriteLine ("ELEMENT {0} {1}: {2}", dimension, i, array [i]);
				print_object (array [i]);
			}
		}

		void print_struct (ITargetStructObject tstruct)
		{
			Console.WriteLine ("STRUCT: {0}", tstruct);
			foreach (ITargetFieldInfo field in tstruct.Type.Fields) {
				Console.WriteLine ("FIELD: {0}", field);
				if (field.Type.HasObject)
					print_object (tstruct.GetField (field.Index));
			}
		}

		void print_class (ITargetClassObject tclass)
		{
			print_struct (tclass);

			ITargetClassObject parent = tclass.Parent;
			if (parent != null) {
				Console.WriteLine ("PARENT");
				print_class (parent);
			}
		}

		void print_pointer (ITargetPointerObject tpointer)
		{
			if (tpointer.Type.IsTypesafe && !tpointer.Type.HasStaticType)
				Console.WriteLine ("CURRENTLY POINTS TO: {0}", tpointer.CurrentType);

			if (tpointer.CurrentType.HasObject)
				Console.WriteLine ("DEREFERENCED: {0}", tpointer.Object);
		}

		void print_type (ITargetType type)
		{
			ITargetArrayType array = type as ITargetArrayType;
			Console.WriteLine ("TYPE: {0}", type);
			if (array != null) {
				Console.WriteLine ("  IS AN ARRAY OF {0}.", array.ElementType);
				return;
			}

			ITargetClassType tclass = type as ITargetClassType;
			if ((tclass != null) && tclass.HasParent)
				Console.WriteLine ("  INHERITS FROM {0}.", tclass.ParentType);

			ITargetStructType tstruct = type as ITargetStructType;
			if (tstruct != null) {
				foreach (ITargetFieldInfo field in tstruct.Fields)
					Console.WriteLine ("  HAS FIELD: {0}", field);
				return;
			}

			ITargetPointerType tpointer = type as ITargetPointerType;
			if (tpointer != null) {
				Console.WriteLine ("  IS A {0}TYPE-SAFE POINTER.", tpointer.IsTypesafe ?
						   "" : "NON-");
				if (tpointer.HasStaticType)
					Console.WriteLine ("  POINTS TO {0}.", tpointer.StaticType);
			}
		}

		void print_object (ITargetObject obj)
		{
			Console.WriteLine ("OBJECT: {0} [{1}]", obj,
					   TargetBinaryReader.HexDump (obj.RawContents));

			if (!obj.Type.HasFixedSize)
				Console.WriteLine ("DYNAMIC CONTENTS: [{0}]",
						   TargetBinaryReader.HexDump (obj.GetRawDynamicContents (-1)));
			
			if (obj.HasObject)
				Console.WriteLine ("OBJECT CONTENTS: |{0}|", obj.Object);

			ITargetArrayObject array = obj as ITargetArrayObject;
			if (array != null) {
				print_array (array, 0);
				return;
			}

			ITargetClassObject tclass = obj as ITargetClassObject;
			if (tclass != null) {
				print_class (tclass);
				return;
			}

			ITargetStructObject tstruct = obj as ITargetStructObject;
			if (tstruct != null) {
				print_struct (tstruct);
				return;
			}

			ITargetPointerObject tpointer = obj as ITargetPointerObject;
			if (tpointer != null) {
				print_pointer (tpointer);
				return;
			}
		}
	}
}
