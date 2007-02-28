using System;
using System.Xml;
using System.Xml.XPath;
using System.Runtime.Serialization;

using Mono.Debugger.Languages;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public class SourceLocation
	{
		public readonly string Name;

		protected readonly string Module;
		protected readonly string Method;

		public readonly string FileName;
		public readonly int Line = -1;

		DynamicSourceLocation dynamic;

		private SourceLocation (DynamicSourceLocation dynamic)
		{
			this.dynamic = dynamic;
		}

		public SourceLocation (TargetFunctionType function)
			: this (new DynamicSourceLocation (function, -1))
		{
			Module = function.Module.Name;
			Method = function.DeclaringType.Name + ':' + function.Name;
			Name = function.FullName;

			if (function.Source != null) {
				FileName = function.Source.SourceFile.FileName;
				Line = function.Source.StartRow;
			}
		}

		public SourceLocation (SourceMethod source)
			: this (source, -1)
		{ }

		public SourceLocation (SourceMethod source, int line)
			: this (new DynamicSourceLocation (source, line))
		{
			Module = source.SourceFile.Module.Name;
			FileName = source.SourceFile.FileName;

			if (source.ClassName != null) {
				string klass = source.ClassName;
				string name = source.Name.Substring (klass.Length + 1);
				Method = klass + ':' + name;
			} else
				Method = source.Name;

			if (line != -1)
				Name = source.Name + ':' + line;
			else
				Name = source.Name;

			Line = line;
		}

		public SourceLocation (SourceFile file, int line)
			: this (new DynamicSourceLocation (file, line))
		{
			Module = file.Module.Name;
			FileName = Name = file.Name + ":" + line;
			Line = line;
		}

		public SourceLocation (string file, int line)
		{
			this.Line = line;
			this.FileName = Name = file + ":" + line;
		}

		public void DumpLineNumbers ()
		{
			if (dynamic == null)
				throw new TargetException (TargetError.LocationInvalid);

			dynamic.DumpLineNumbers ();
		}

		protected bool Resolve (DebuggerSession session, Thread target)
		{
			if (dynamic != null)
				return true;

			if (Method != null) {
				Module module = session.GetModule (Module);

				int pos = Method.IndexOf (':');
				if (pos > 0) {
					string class_name = Method.Substring (0, pos);
					string method_name = Method.Substring (pos + 1);

					dynamic = new DynamicSourceLocation (
						module.LookupMethod (class_name, method_name), Line);
				} else {
					dynamic = new DynamicSourceLocation (
						module.FindMethod (Method), Line);
				}

				return true;
			}

			if (FileName != null) {
				int pos = FileName.IndexOf (':');
				if (pos < 0)
					return false;

				string filename = FileName.Substring (0, pos);

				SourceFile file = target.Process.FindFile (filename);
				if (file == null)
					return false;

				dynamic = new DynamicSourceLocation (file, Line);
				return true;
			}

			return false;
		}

		internal BreakpointHandle InsertBreakpoint (DebuggerSession session,
							    Thread target, Breakpoint breakpoint,
							    int domain)
		{
			if (!Resolve (session, target))
				throw new TargetException (TargetError.LocationInvalid);

			return dynamic.InsertBreakpoint (target, breakpoint, domain);
		}

		internal void OnTargetExited ()
		{
			dynamic = null;
		}

		internal void GetSessionData (XmlElement root)
		{
			XmlElement name_e = root.OwnerDocument.CreateElement ("Name");
			name_e.InnerText = Name;
			root.AppendChild (name_e);

			if (Module != null) {
				XmlElement module_e = root.OwnerDocument.CreateElement ("Module");
				module_e.InnerText = Module;
				root.AppendChild (module_e);
			}

			if (Method != null) {
				XmlElement method_e = root.OwnerDocument.CreateElement ("Method");
				method_e.InnerText = Method;
				root.AppendChild (method_e);
			}

			if (FileName != null) {
				XmlElement file_e = root.OwnerDocument.CreateElement ("File");
				file_e.InnerText = FileName;
				root.AppendChild (file_e);
			}

			if (Line > 0) {
				XmlElement line_e = root.OwnerDocument.CreateElement ("Line");
				line_e.InnerText = Line.ToString ();
				root.AppendChild (line_e);
			}
		}

		internal SourceLocation (DebuggerSession session, XPathNavigator navigator)
		{
			this.Line = -1;

			XPathNodeIterator children = navigator.SelectChildren (XPathNodeType.Element);
			while (children.MoveNext ()) {
				if (children.Current.Name == "Module")
					Module = children.Current.Value;
				else if (children.Current.Name == "Method")
					Method = children.Current.Value;
				else if (children.Current.Name == "File")
					FileName = children.Current.Value;
				else if (children.Current.Name == "Name")
					Name = children.Current.Value;
				else if (children.Current.Name == "Line")
					Line = Int32.Parse (children.Current.Value);
				else
					throw new InvalidOperationException ();
			}
		}
	}

	// <summary>
	//   Represents a location in the source code on which we can insert a breakpoint.
	//   Instances of this class are normally created as the result of a user action
	//   such as a method lookup.
	// </summary>
	internal class DynamicSourceLocation
	{
		Module module;
		SourceFile file;
		SourceMethod source;
		TargetFunctionType function;
		string method;
		int line;

		public DynamicSourceLocation (SourceMethod source)
			: this (source, -1)
		{ }

		public DynamicSourceLocation (SourceMethod source, int line)
		{
			this.module = source.SourceFile.Module;
			this.file = source.SourceFile;
			this.source = source;
			this.line = line;
		}

		public DynamicSourceLocation (SourceFile file, int line)
		{
			this.module = file.Module;
			this.file = file;
			this.line = line;
		}

		public DynamicSourceLocation (TargetFunctionType function, int line)
		{
			this.function = function;
			this.module = function.Module;
			this.source = function.Source;

			if (source != null)
				file = source.SourceFile;

			this.line = line;
		}

		internal void DumpLineNumbers ()
		{
			if (source == null)
				throw new TargetException (TargetError.LocationInvalid);

			Method method = source.GetMethod (0);
			if ((method == null) || !method.HasSource)
				throw new TargetException (TargetError.LocationInvalid);

			method.Source.DumpLineNumbers ();
		}

		internal BreakpointHandle InsertBreakpoint (Thread target, Breakpoint breakpoint,
							    int domain)
		{
			if (!module.IsLoaded)
				return new ModuleBreakpointHandle (breakpoint, this);

			if ((function == null) && (source == null)) {
				if (method != null) {
					int pos = method.IndexOf (':');
					if (pos > 0) {
						string class_name = method.Substring (0, pos);
						string method_name = method.Substring (pos + 1);

						function = module.LookupMethod (class_name, method_name);
					} else {
						source = module.FindMethod (method);
					}
				} else if (file != null) {
					source = file.FindMethod (line);
				} else {
					throw new TargetException (TargetError.LocationInvalid);
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

		private class ModuleBreakpointHandle : BreakpointHandle
		{
			DynamicSourceLocation location;

			public ModuleBreakpointHandle (Breakpoint bpt, DynamicSourceLocation location)
				: base (bpt)
			{
				this.location = location;

				location.module.ModuleLoadedEvent += module_loaded;
			}

			void module_loaded (Module module)
			{
				Console.WriteLine ("MODULE LOADED: {0} {1}", module, location);
			}

			public override void Remove (Thread target)
			{
				location.module.ModuleLoadedEvent -= module_loaded;
			}
		}

		private class FunctionBreakpointHandle : BreakpointHandle
		{
			ILoadHandler load_handler;
			int index = -1;
			int domain;

			public FunctionBreakpointHandle (Thread target, Breakpoint bpt, int domain,
							 DynamicSourceLocation location)

				: base (bpt)
			{
				this.domain = domain;

				load_handler = location.module.SymbolFile.RegisterLoadHandler (
					target, location.source, method_loaded, location);
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

				DynamicSourceLocation location = (DynamicSourceLocation) data;
				TargetAddress address = location.GetAddress (domain);
				if (address.IsNull)
					return;

				try {
					index = target.InsertBreakpoint (Breakpoint, address);
				} catch (TargetException ex) {
					Report.Error ("Can't insert breakpoint {0} at {1}: {2}",
						      Breakpoint.Index, address, ex.Message);
					index = -1;
				}
			}
		}
	}
}
