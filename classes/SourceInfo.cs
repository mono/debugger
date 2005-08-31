using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	// <summary>
	//   A single source file.  It is used to find a breakpoint's location by method
	//   name or source file.
	// </summary>
	public class SourceFile : MarshalByRefObject
	{
		public string Name {
			get {
				return Path.GetFileName (filename);
			}
		}

		// <summary>
		//   The file's full pathname.
		// </summary>
		public string FileName {
			get {
				return Path.GetFullPath (filename);
			}
		}

		public Module Module {
			get {
				return module;
			}
		}

		public int ID {
			get {
				return id;
			}
		}

		public void AddMethod (SourceMethod method)
		{
			methods.Add (method);
		}

		// <summary>
		//   Returns a list of SourceMethod's which is sorted by source lines.
		//   It is used when inserting a breakpoint by source line to find the
		//   method this line is contained in.
		// </summary>
		public SourceMethod[] Methods {
			get {
				module.SymbolFile.GetMethods (this);
				SourceMethod[] retval = new SourceMethod [methods.Count];
				methods.CopyTo (retval, 0);
				return retval;
			}
		}

		public SourceLocation FindLine (int line)
		{
			module.SymbolFile.GetMethods (this);
			foreach (SourceMethod method in methods) {
				if ((method.StartRow <= line) && (method.EndRow >= line))
					return new SourceLocation (method, line);
			}

			return null;
		}

		public SourceFile (Module module, string filename)
		{
			this.id = ++next_id;
			this.module = module;
			this.filename = filename;
			this.methods = new ArrayList ();
		}

		string filename;
		Module module;
		int id;
		ArrayList methods;
		static int next_id = 0;

		public override string ToString ()
		{
			return String.Format ("SourceFile ({0}:{1})", ID, FileName);
		}
	}

	internal delegate void MethodLoadedHandler (ITargetMemoryAccess target, SourceMethod method,
						    object user_data);

	// <summary>
	//   This is a handle to a method which persists across different invocations of
	//   the same target and which doesn't consume too much memory.
	// </summary>
	public class SourceMethod : MarshalByRefObject
	{
		public long Handle {
			get {
				return handle;
			}
		}

		public SourceFile SourceFile {
			get {
				return source;
			}
		}

		public string Name {
			get {
				return name;
			}
		}

		public int StartRow {
			get {
				return start_row;
			}
		}

		public int EndRow {
			get {
				return end_row;
			}
		}

		// <summary>
		//   Whether this method is current loaded in memory.  For managed
		//   methods, this returns whether the method has already been JITed.
		// </summary>
		public bool IsLoaded {
			get {
				if (method != null)
					return true;

				method = module.SymbolFile.GetMethod (handle);
				return method != null;
			}
		}

		// <summary>
		//   May only be used while the method is loaded and return's the IMethod.
		//
		//   Throws:
		//     InvalidOperationException - IsLoaded is false.
		// </summary>
		public IMethod Method {
			get {
				if (!IsLoaded)
					throw new InvalidOperationException ();

				return method;
			}
		}

		// <summary>
		//   If true, you may use RegisterLoadHandler() to register a delegate to
		//   be invoked when the method is loaded.
		// </summary>
		// <remarks>
		//   If both IsLoaded and IsDynamic are false, then the method isn't
		//   currently loaded, but there's also no way of getting a notification.
		// </remarks>
		public bool IsDynamic {
			get {
				return is_dynamic;
			}
		}

		public TargetAddress Lookup (int SourceLine)
		{
			if (!IsLoaded)
				throw new InvalidOperationException ();

			if (Method.HasSource)
				return Method.Source.Lookup (SourceLine);
			else
				return TargetAddress.Null;
		}

		public override string ToString ()
		{
			return String.Format ("Method ({0}:{1}:{2}:{3}:{4})", Name, SourceFile,
					      StartRow, EndRow, IsLoaded);
		}

		IMethod method;
		Module module;
		SourceFile source;
		string name;
		int  start_row, end_row;
		bool is_dynamic;
		long handle;

		public SourceMethod (Module module, SourceFile source,
				     long handle, string name, int start, int end,
				     bool is_dynamic)
		{
			this.module = module;
			this.source = source;
			this.handle = handle;
			this.name = name;
			this.start_row = start;
			this.end_row = end;
			this.is_dynamic = is_dynamic;

			source.AddMethod (this);
		}
	}
}
