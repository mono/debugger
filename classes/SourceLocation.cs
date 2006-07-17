using System;
using System.Xml;
using System.Xml.XPath;
using System.Runtime.Serialization;

using Mono.Debugger.Languages;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	// <summary>
	//   Represents a location in the source code on which we can insert a breakpoint.
	//   Instances of this class are normally created as the result of a user action
	//   such as a method lookup.
	// </summary>
	[Serializable]
	public class SourceLocation
	{
		Module module;
		SourceFile file;
		SourceMethod source;
		TargetFunctionType function;
		string method;
		int line;

		public Module Module {
			get { return module; }
		}

		public bool HasSourceFile {
			get { return file != null; }
		}

		public bool HasMethod {
			get { return source != null; }
		}

		public bool HasLine {
			get { return line != -1; }
		}

		public bool HasFunction {
			get { return function != null; }
		}

		public SourceFile SourceFile {
			get {
				if (!HasSourceFile)
					throw new InvalidOperationException ();

				return file;
			}
		}

		public SourceMethod Method {
			get {
				if (!HasMethod)
					throw new InvalidOperationException ();

				return source;
			}
		}

		public TargetFunctionType Function {
			get {
				if (!HasFunction)
					throw new InvalidOperationException ();

				return function;
			}
		}

		public int Line {
			get {
				if (line == -1)
					return source.StartRow;
				else
					return line;
			}
		}

		public string Name {
			get {
				if (function != null)
					return function.FullName;
				else if (source != null) {
					if (line != -1)
						return source.Name + ':' + line;
					else
						return source.Name;
				} else if (file != null)
					return SourceFile.FileName + ':' + line;
				else
					return method;
			}
		}

		public SourceLocation (SourceMethod source)
			: this (source, -1)
		{ }

		public SourceLocation (SourceMethod source, int line)
		{
			this.module = source.SourceFile.Module;
			this.file = source.SourceFile;
			this.source = source;
			this.line = line;
		}

		public SourceLocation (SourceFile file, int line)
		{
			this.module = file.Module;
			this.file = file;
			this.line = line;
		}

		public SourceLocation (TargetFunctionType function)
		{
			this.function = function;
			this.module = function.Module;
			this.source = function.Source;

			if (source != null)
				file = source.SourceFile;

			this.line = -1;
		}

		internal SourceLocation (DebuggerSession session, XPathNavigator navigator)
		{
			this.line = -1;

			XPathNodeIterator children = navigator.SelectChildren (XPathNodeType.Element);
			while (children.MoveNext ()) {
				if (children.Current.Name == "Module")
					module = session.GetModule (children.Current.Value);
				else if (children.Current.Name == "Method")
					method = children.Current.Value;
				else if (children.Current.Name == "Line")
					line = Int32.Parse (children.Current.Value);
				else
					throw new InvalidOperationException ();
			}
		}

		internal BreakpointHandle InsertBreakpoint (Thread target, Breakpoint breakpoint,
							    int domain)
		{
			if (!module.IsLoaded)
				return new ModuleBreakpointHandle (breakpoint, this);

			if ((function == null) && (source == null)) {
				if (method == null)
					throw new TargetException (TargetError.LocationInvalid);

				int pos = method.IndexOf (':');
				if (pos > 0) {
					string class_name = method.Substring (0, pos);
					string method_name = method.Substring (pos + 1);

					function = module.LookupMethod (class_name, method_name);
				} else {
					source = module.FindMethod (method);
				}
			}

			if (function != null) {
				if (line > 0)
					source = function.Source;
				else if (function.IsLoaded) {
					int index = target.InsertBreakpoint (breakpoint, function);
					return new SimpleBreakpointHandle (breakpoint, index);
				} else {
					source = function.Source;
					return new FunctionBreakpointHandle (
						target, breakpoint, domain, this);
				}
			}

			if (source == null)
				throw new TargetException (TargetError.LocationInvalid);

			TargetAddress address = GetAddress (domain);
			if (!address.IsNull) {
				int index = target.InsertBreakpoint (breakpoint, address);
				return new SimpleBreakpointHandle (breakpoint, index);
			} else if (source.IsDynamic) {
				// A dynamic method is a method which may emit a
				// callback when it's loaded.  We register this
				// callback here and do the actual insertion when
				// the method is loaded.
				return new FunctionBreakpointHandle (
					target, breakpoint, domain, this);
			}

			return null;
		}

		protected TargetAddress GetAddress (int domain)
		{
			if (source == null)
				return TargetAddress.Null;

			Method method = source.GetMethod (domain);
			if (method == null)
				return TargetAddress.Null;

			if (line != -1) {
				if (method.HasSource)
					return method.Source.Lookup (line);
				else
					return TargetAddress.Null;
			} else if (method.HasMethodBounds)
				return method.MethodStartAddress;
			else
				return method.StartAddress;
		}

		//
		// Session handling.
		//

		internal void GetSessionData (XmlElement root)
		{
			if (function != null) {
				XmlElement module = root.OwnerDocument.CreateElement ("Module");
				module.InnerText = function.Module.Name;
				root.AppendChild (module);

				XmlElement method_e = root.OwnerDocument.CreateElement ("Method");
				method_e.InnerText = function.DeclaringType.Name + ':' + function.Name;
				root.AppendChild (method_e);
			} else if (source != null) {
				XmlElement module = root.OwnerDocument.CreateElement ("Module");
				module.InnerText = source.SourceFile.Module.Name;
				root.AppendChild (module);

				XmlElement method_e = root.OwnerDocument.CreateElement ("Method");
				root.AppendChild (method_e);

				if (source.ClassName != null) {
					string klass = source.ClassName;
					string name = source.Name.Substring (klass.Length + 1);
					method_e.InnerText = klass + ':' + name;
				} else
					method_e.InnerText = source.Name;
			} else if (file != null) {
				XmlElement module = root.OwnerDocument.CreateElement ("Module");
				module.InnerText = file.Module.Name;
				root.AppendChild (module);

				XmlElement file_e = root.OwnerDocument.CreateElement ("File");
				file_e.InnerText = file.Name + ":" + line;
				root.AppendChild (file_e);
			} else if (method != null) {
				XmlElement module = root.OwnerDocument.CreateElement ("Module");
				module.InnerText = module.Name;
				root.AppendChild (module);

				XmlElement method_e = root.OwnerDocument.CreateElement ("Method");
				method_e.InnerText = method;
				root.AppendChild (method_e);
			} else {
				throw new InternalError ();
			}

			if (line > 0) {
				XmlElement line_e = root.OwnerDocument.CreateElement ("Line");
				line_e.InnerText = line.ToString ();
				root.AppendChild (line_e);
			}
		}

		private class ModuleBreakpointHandle : BreakpointHandle
		{
			SourceLocation location;

			public ModuleBreakpointHandle (Breakpoint bpt, SourceLocation location)
				: base (bpt)
			{
				this.location = location;

				location.Module.ModuleLoadedEvent += module_loaded;
			}

			void module_loaded (Module module)
			{
				Console.WriteLine ("MODULE LOADED: {0} {1}", module, location);
			}

			public override void Remove (Thread target)
			{
				location.Module.ModuleLoadedEvent -= module_loaded;
			}
		}

		private class FunctionBreakpointHandle : BreakpointHandle
		{
			ILoadHandler load_handler;
			int index = -1;
			int domain;

			public FunctionBreakpointHandle (Thread target, Breakpoint bpt, int domain,
							 SourceLocation location)

				: base (bpt)
			{
				this.domain = domain;

				load_handler = location.Module.SymbolFile.RegisterLoadHandler (
					target, location.Method, method_loaded, location);
			}

			public override void Remove (Thread target)
			{
				if (index > 0)
					target.RemoveBreakpoint (index);

				if (load_handler != null)
					load_handler.Remove ();

				load_handler = null;
				index = -1;
			}

			// <summary>
			//   The method has just been loaded, lookup the breakpoint
			//   address and actually insert it.
			// </summary>
			public void method_loaded (TargetMemoryAccess target,
						   SourceMethod source, object data)
			{
				load_handler = null;

				SourceLocation location = (SourceLocation) data;
				TargetAddress address = location.GetAddress (domain);
				if (address.IsNull)
					return;

				index = target.InsertBreakpoint (Breakpoint, address);
			}
		}

		public override string ToString ()
		{
			return String.Format ("SourceLocation ({0})", Name);
		}
	}
}
