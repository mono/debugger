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
			Method = function.FullName;
			Name = function.FullName;

			if (function.Source != null) {
				FileName = function.Source.SourceFile.FileName;
				Line = function.Source.StartRow;
			}
		}

		public SourceLocation (MethodSource source)
			: this (source, -1)
		{ }

		public SourceLocation (MethodSource source, int line)
			: this (new DynamicSourceLocation (source, line))
		{
			Module = source.SourceFile.Module.Name;
			FileName = source.SourceFile.FileName;
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
			FileName = file.FileName;
			Name = file.FileName + ":" + line;
			Line = line;
		}

		public SourceLocation (string file, int line)
		{
			this.Line = line;
			this.FileName = file;
			this.Name = file + ":" + line;
		}

		public void DumpLineNumbers ()
		{
			if (dynamic == null)
				throw new TargetException (TargetError.LocationInvalid);

			dynamic.DumpLineNumbers ();
		}

		protected bool Resolve (DebuggerSession session)
		{
			if (dynamic != null)
				return true;

			if (Method != null) {
				Module module = session.GetModule (Module);
				MethodSource source = module.FindMethod (Method);

				if (source == null)
					return false;

				dynamic = new DynamicSourceLocation (source, Line);
				return true;
			}

			if (FileName != null) {
				int pos = FileName.IndexOf (':');
				if (pos < 0)
					return false;

				string filename = FileName.Substring (0, pos);

				SourceFile file = session.FindFile (filename);
				if (file == null)
					return false;

				dynamic = new DynamicSourceLocation (file, Line);
				return true;
			}

			return false;
		}

		internal BreakpointHandle ResolveBreakpoint (DebuggerSession session,
							     Breakpoint breakpoint, int domain)
		{
			if (!Resolve (session))
				throw new TargetException (TargetError.LocationInvalid);

			return dynamic.ResolveBreakpoint (breakpoint, domain);
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
		MethodSource source;
		TargetFunctionType function;
		string method;
		int line;

		public DynamicSourceLocation (MethodSource source)
			: this (source, -1)
		{ }

		public DynamicSourceLocation (MethodSource source, int line)
		{
			this.module = source.Module;
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
			if ((method == null) || !method.HasLineNumbers)
				throw new TargetException (TargetError.LocationInvalid);

			method.LineNumberTable.DumpLineNumbers ();
		}

		internal BreakpointHandle ResolveBreakpoint (Breakpoint breakpoint, int domain)
		{
			if (!module.IsLoaded)
				return new ModuleBreakpointHandle (breakpoint, module);

			if ((function == null) && (source == null)) {
				if (method != null) {
					source = module.FindMethod (method);
				} else if (file != null) {
					source = file.FindMethod (line);
				} else {
					throw new TargetException (TargetError.LocationInvalid);
				}
			}

			if (function != null) {
				if (line > 0)
					source = function.Source;
				else if (function.IsLoaded)
					return new FunctionBreakpointHandle (breakpoint, domain, function);
				else {
					source = function.Source;
					return new FunctionBreakpointHandle (breakpoint, domain, source);
				}
			}

			if (source == null)
				throw new TargetException (TargetError.LocationInvalid);

			TargetAddress address = GetAddress (domain);
			if (!address.IsNull) {
				return new AddressBreakpointHandle (breakpoint, address);
			} else if (source.IsManaged) {
				return new FunctionBreakpointHandle (breakpoint, domain, source, line);
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
				if (method.HasLineNumbers)
					return method.LineNumberTable.Lookup (line);
				else
					return TargetAddress.Null;
			} else if (method.HasMethodBounds)
				return method.MethodStartAddress;
			else
				return method.StartAddress;
		}

		class ModuleBreakpointHandle : BreakpointHandle
		{
			Module module;

			public ModuleBreakpointHandle (Breakpoint bpt, Module module)
				: base (bpt)
			{
				this.module = module;
			}

			void module_loaded (Module module)
			{
				Console.WriteLine ("MODULE LOADED: {0} {1}", module);
			}

			public override void Insert (Thread target)
			{
				module.ModuleLoadedEvent += module_loaded;
			}

			public override void Remove (Thread target)
			{
				module.ModuleLoadedEvent -= module_loaded;
			}
		}
	}
}
