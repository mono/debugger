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
			Console.WriteLine ("FIND METHOD: {0} {1}", this, name);
			if (data == null)
				return null;

			return (SourceMethodInfo) data.MethodHash [name];
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
		}

		string filename;
		Module module;

		public override string ToString ()
		{
			return String.Format ("SourceInfo ({0})", FileName);
		}

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
