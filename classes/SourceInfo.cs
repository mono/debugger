using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	public abstract class SourceInfo
	{
		public string FileName {
			get {
				return filename;
			}
		}

		public Module Module {
			get {
				return module;
			}
		}

		public void AddMethod (SourceMethodInfo method)
		{
			if (!methods.Contains (method.Name))
				methods.Add (method.Name, method);
		}

		public SourceMethodInfo[] Methods {
			get {
				SourceMethodInfo[] retval = new SourceMethodInfo [methods.Values.Count];
				methods.Values.CopyTo (retval, 0);
				return retval;
			}
		}

		public SourceMethodInfo FindMethod (string name)
		{
			return (SourceMethodInfo) methods [name];
		}

		// <summary>
		//   Returns the method in which @SourceLine is or null if the
		//   line is in no method.
		// </summary>
		public abstract ITargetLocation Lookup (int SourceLine);

		protected SourceInfo (Module module, string filename)
		{
			this.module = module;
			this.filename = filename;

			methods = new Hashtable ();
		}

		string filename;
		Hashtable methods;
		Module module;

		public override string ToString ()
		{
			return String.Format ("SourceInfo ({0})", FileName);
		}
	}

	public delegate void MethodLoadedHandler (SourceMethodInfo method, object user_data);

	public abstract class SourceMethodInfo
	{
		public SourceInfo SourceInfo {
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

		public abstract bool IsLoaded {
			get;
		}

		public abstract IMethod Method {
			get;
		}

		public bool IsDynamic {
			get {
				return is_dynamic;
			}
		}

		// <summary>
		//   Registers a delegate to be invoked when the method is loaded.
		//   This is an expensive operation and must not be used in a GUI to get
		//   a notification when the `IsLoaded' field changed.
		// </summary>
		public abstract IDisposable RegisterLoadHandler (MethodLoadedHandler handler,
								 object user_data);

		public override string ToString ()
		{
			return String.Format ("Method ({0}:{1}:{2}:{3}:{4})", Name, SourceInfo,
					      StartRow, EndRow, IsLoaded);
		}

		SourceInfo source;
		string name;
		int start_row, end_row;
		bool is_dynamic;

		protected SourceMethodInfo (SourceInfo source, string name, int start, int end,
					    bool is_dynamic)
		{
			this.source = source;
			this.name = name;
			this.start_row = start;
			this.end_row = end;
			this.is_dynamic = is_dynamic;
		}
	}
}
