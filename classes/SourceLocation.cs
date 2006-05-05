using System;
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
	public class SourceLocation : IDeserializationCallback
	{
		Module module;
		SourceFile file;
		SourceMethod source;
		TargetFunctionType function;
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
					return function.Name;
				else if (line == -1)
					return source.Name;
				else
					return String.Format ("{0}:{1}", SourceFile.FileName, line);
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
			this.file = function.Source.SourceFile;
			this.line = -1;
		}

		internal BreakpointHandle InsertBreakpoint (Thread target, Breakpoint breakpoint,
							    int domain)
		{
			if (function != null) {
				if (function.IsLoaded) {
					int index = target.InsertBreakpoint (breakpoint, function);
					return new SimpleBreakpointHandle (breakpoint, index);
				} else
					return new FunctionBreakpointHandle (
						target, breakpoint, domain, this);
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

		void IDeserializationCallback.OnDeserialization (object sender)
		{
			if (function != null) {
				this.module = function.Module;
				this.source = function.Source;
			} else if (source != null) {
				this.module = source.SourceFile.Module;
			} else if (file != null) {
				this.module = file.Module;
			}
		}

		protected virtual void GetSessionData (SerializationInfo info)
		{
			if (function != null) {
				info.AddValue ("type", "function");
				info.AddValue ("function", function);
			} else if (source != null) {
				info.AddValue ("type", "source");
				info.AddValue ("source", source);
				info.AddValue ("line", line);
			} else if (file != null) {
				info.AddValue ("type", "file");
				info.AddValue ("file", file);
				info.AddValue ("line", line);
			} else
				info.AddValue ("type", "unknown");
		}

		protected virtual void SetSessionData (SerializationInfo info)
		{
			string type = info.GetString ("type");
			if (type == "source") {
				source = (SourceMethod) info.GetValue (
					"source", typeof (SourceMethod));
				line = info.GetInt32 ("line");
			} else if (type == "file") {
				file = (SourceFile) info.GetValue (
					"file", typeof (SourceFile));
				line = info.GetInt32 ("line");
			} else if (type == "function") {
				function = (TargetFunctionType) info.GetValue (
					"function", typeof (TargetFunctionType));
				line = -1;
			} else
				throw new InvalidOperationException ();
		}

		protected internal class SessionSurrogate : ISerializationSurrogate
		{
			public virtual void GetObjectData (object obj, SerializationInfo info,
							   StreamingContext context)
			{
				SourceLocation location = (SourceLocation) obj;
				location.GetSessionData (info);
			}

			public object SetObjectData (object obj, SerializationInfo info,
						     StreamingContext context,
						     ISurrogateSelector selector)
			{
				SourceLocation location = (SourceLocation) obj;
				location.SetSessionData (info);
				return location;
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

				load_handler = location.Module.RegisterLoadHandler (
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
