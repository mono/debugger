using System;
using System.Xml;
using System.Xml.XPath;
using System.Runtime.Serialization;

using Mono.Debugger.Languages;
using Mono.Debugger.Backend;

namespace Mono.Debugger
{
	public class SourceLocation
	{
		public readonly string Name;

		protected readonly string Module;
		protected readonly string Method;

		public readonly string FileName;
		public readonly int Line = -1;
		public readonly int Column = -1;

		DynamicSourceLocation dynamic;

		private SourceLocation (DynamicSourceLocation dynamic)
		{
			this.dynamic = dynamic;
		}

		public SourceLocation (TargetFunctionType function)
			: this (new DynamicSourceLocation (function, -1, -1))
		{
			Module = function.Module.Name;
			Method = function.FullName;
			Name = function.FullName;

			MethodSource source = function.GetSourceCode ();
			if (source != null)
				FileName = source.SourceFile.FileName;
		}

		public SourceLocation (MethodSource source)
			: this (source, source.SourceFile, -1, -1)
		{ }

		public SourceLocation (MethodSource source, SourceFile file, int line)
			: this (source, file, line, -1)
		{ }

		public SourceLocation (MethodSource source, SourceFile file, int line, int column)
			: this (new DynamicSourceLocation (source, file, line, column))
		{
			Module = file.Module.Name;
			FileName = file.FileName;
			Method = source.Name;

			if (line != -1)
				Name = source.Name + ':' + line;
			else
				Name = source.Name;

			Line = line;
			Column = column;
		}

		public SourceLocation (SourceFile file, int line)
			: this (file, line, -1)
		{ }

		public SourceLocation (SourceFile file, int line, int column)
			: this (new DynamicSourceLocation (file, line, column))
		{
			Module = file.Module.Name;
			FileName = file.FileName;
			Name = file.FileName + ":" + line;
			Line = line;
			Column = column;
		}

		public SourceLocation (string file, int line)
		{
			this.Line = line;
			this.FileName = file;
			this.Name = file + ":" + line;
		}

		public SourceLocation (string file, int line, int column)
		{
			this.Line = line;
			this.Column = column;
			this.FileName = file;
			this.Name = file + ":" + line + ":" + column;
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

				dynamic = new DynamicSourceLocation (source, source.SourceFile, Line, Column);
				return true;
			}

			if (FileName != null) {
				SourceFile file = session.FindFile (FileName);
				if (file == null)
					return false;

				dynamic = new DynamicSourceLocation (file, Line, Column);
				return true;
			}

			return false;
		}

		internal BreakpointHandle ResolveBreakpoint (DebuggerSession session,
							     Breakpoint breakpoint)
		{
			if (!Resolve (session))
				throw new TargetException (TargetError.LocationInvalid);

			return dynamic.ResolveBreakpoint (breakpoint);
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

			if (Column > 0) {
				XmlElement col_e = root.OwnerDocument.CreateElement ("Column");
				col_e.InnerText = Column.ToString ();
				root.AppendChild (col_e);
			}
		}

		internal SourceLocation (DebuggerSession session, XPathNavigator navigator)
		{
			this.Line = -1;
			this.Column = -1;

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
				else if (children.Current.Name == "Column")
					Column = Int32.Parse (children.Current.Value);
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
		int line, column;

		public DynamicSourceLocation (MethodSource source)
			: this (source, source.SourceFile, -1, -1)
		{ }

		public DynamicSourceLocation (MethodSource source, SourceFile file, int line, int column)
		{
			if (source.IsManaged) {
				this.function = source.Function;
				this.module = function.Module;
			} else {
				this.module = source.Module;
				this.source = source;
			}

			this.file = file;
			this.line = line;
			this.column = column;
		}

		public DynamicSourceLocation (SourceFile file, int line, int column)
		{
			this.module = file.Module;
			this.file = file;
			this.line = line;
			this.column = column;
		}

		public DynamicSourceLocation (TargetFunctionType function, int line, int column)
		{
			this.function = function;
			this.file = null;
			this.module = function.Module;

			this.line = line;
			this.column = column;
		}

		internal BreakpointHandle ResolveBreakpoint (Breakpoint breakpoint)
		{
			if (!module.IsLoaded)
				return null;

			if ((function == null) && (source == null)) {
				if (file != null) {
					source = file.FindMethod (line);
				} else {
					throw new TargetException (TargetError.LocationInvalid);
				}

				if ((source != null) && source.IsManaged)
					function = source.Function;
			}

			if (function != null)
				return function.GetBreakpointHandle (breakpoint, line, column);

			if ((source == null) || source.IsManaged)
				throw new TargetException (TargetError.LocationInvalid);

			TargetAddress address = GetAddress ();
			if (!address.IsNull)
				return new AddressBreakpointHandle (breakpoint, address);

			return null;
		}

		protected TargetAddress GetAddress ()
		{
			if ((source == null) || source.IsManaged)
				return TargetAddress.Null;

			Method method = source.NativeMethod;
			if (method == null)
				return TargetAddress.Null;

			if (line != -1) {
				if (method.HasLineNumbers)
					return method.LineNumberTable.Lookup (line, column);
				else
					return TargetAddress.Null;
			} else if (method.HasMethodBounds)
				return method.MethodStartAddress;
			else
				return method.StartAddress;
		}
	}
}
