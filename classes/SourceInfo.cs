using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace Mono.Debugger
{
	// <summary>
	//   A single source file.  It is used to find a breakpoint's location by method
	//   name or source file.
	// </summary>
	public abstract class SourceInfo
	{
		// <summary>
		//   The file's full pathname.
		// </summary>
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

		protected abstract ArrayList GetMethods ();

		ObjectCache method_cache = null;
		SourceData ensure_methods ()
		{
			lock (this) {
				if (method_cache == null)
					method_cache = new ObjectCache
						(new ObjectCacheFunc (get_methods), null,
						 new TimeSpan (0,1,0));

				return (SourceData) method_cache.Data;
			}
		}

		object get_methods (object user_data)
		{
			lock (this) {
				ArrayList methods = GetMethods ();
				if (methods == null)
					return null;

				Hashtable method_hash = new Hashtable ();
				foreach (SourceMethodInfo method in methods) {
					if (!method_hash.Contains (method.Name))
						method_hash.Add (method.Name, method);
				}

				return new SourceData (methods, method_hash);
			}
		}

		// <summary>
		//   Returns a list of SourceMethodInfo's which is sorted by source lines.
		//   It is used when inserting a breakpoint by source line to find the
		//   method this line is contained in.
		// </summary>
		public SourceMethodInfo[] Methods {
			get {
				SourceData data = ensure_methods ();
				if (data == null)
					return new SourceMethodInfo [0];

				SourceMethodInfo[] retval = new SourceMethodInfo [data.Methods.Count];
				data.Methods.CopyTo (retval, 0);
				return retval;
			}
		}

		public SourceMethodInfo FindMethod (string name)
		{
			SourceData data = ensure_methods ();
			if (data == null)
				return null;

			return (SourceMethodInfo) data.MethodHash [name];
		}

		public SourceMethodInfo FindMethod (int line)
		{
			SourceData data = ensure_methods ();
			if (data == null)
				return null;

			foreach (SourceMethodInfo method in data.Methods) {
				if ((method.StartRow <= line) && (method.EndRow >= line))
					return method;
			}

			return null;
		}

		protected SourceInfo (Module module, string filename)
		{
			this.module = module;
			this.filename = filename;
		}

		string filename;
		Module module;

		public override string ToString ()
		{
			return String.Format ("SourceInfo ({0})", FileName);
		}

		// <remarks>
		//   This is cached in a weak reference; `Methods' is a list of
		//   SourceMethodInfo's, sorted by their start lines and `MethodHash' maps
		//   the method's full name to a SourceMethodInfo.
		// </remarks>
		private class SourceData
		{
			public readonly ArrayList Methods;
			public readonly Hashtable MethodHash;

			public SourceData (ArrayList methods, Hashtable method_hash)
			{
				this.Methods = methods;
				this.MethodHash = method_hash;
			}
		}
	}

	public delegate void MethodLoadedHandler (SourceMethodInfo method, object user_data);

	// <summary>
	//   This is a handle to a method which persists across different invocations of
	//   the same target and which doesn't consume too much memory.
	// </summary>
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

		// <summary>
		//   Whether this method is current loaded in memory.  For managed
		//   methods, this returns whether the method has already been JITed.
		// </summary>
		public abstract bool IsLoaded {
			get;
		}

		// <summary>
		//   May only be used while the method is loaded and return's the IMethod.
		//
		//   Throws:
		//     InvalidOperationException - IsLoaded is false.
		// </summary>
		public abstract IMethod Method {
			get;
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

		public abstract TargetAddress Lookup (int SourceLine);

		// <summary>
		//   Registers a delegate to be invoked when the method is loaded.
		//   This is an expensive operation and must not be used in a GUI to get
		//   a notification when the `IsLoaded' field changed.
		//
		//   This is an expensive operation, registering too many load handlers
		//   may slow that target down, so do not use this in the user interface
		//   to get any notifications when a method is loaded or something like
		//   this.  It's just intended to insert breakpoints.
		//
		//   To unregister the delegate, dispose the returned IDisposable.
		//
		//   Throws:
		//     InvalidOperationException - IsDynamic was false or IsLoaded was true
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
