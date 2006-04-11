using System;
using System.Runtime.Serialization;

using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	// <summary>
	//   Represents a location in the source code on which we can insert a breakpoint.
	//   Instances of this class are normally created as the result of a user action
	//   such as a method lookup.
	// </summary>
	public class SourceLocation : IDeserializationCallback
	{
		Module module;
		SourceMethod source;
		TargetFunctionType function;
		int line;

		public Module Module {
			get { return module; }
		}

		public bool HasSourceFile {
			get { return source != null; }
		}

		public bool HasFunction {
			get { return function != null; }
		}

		public SourceFile SourceFile {
			get {
				if (!HasSourceFile)
					throw new InvalidOperationException ();

				return source.SourceFile;
			}
		}

		public SourceMethod Method {
			get {
				if (!HasSourceFile)
					throw new InvalidOperationException ();

				return source;
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
			this.source = source;
			this.line = line;

			if (source == null)
				throw new InvalidOperationException ();
		}

		public SourceLocation (TargetFunctionType function)
		{
			this.function = function;
			this.module = function.Module;
			this.source = function.Source;
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
				return null;

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
			} else
				info.AddValue ("type", "unknown");
		}

		protected virtual void SetSessionData (SerializationInfo info, Process process)
		{
			string type = info.GetString ("type");
			if (type == "source")
				source = (SourceMethod) info.GetValue (
					"source", typeof (SourceMethod));
			else if (type == "function")
				function = (TargetFunctionType) info.GetValue (
					"function", typeof (TargetFunctionType));
			else
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
				location.SetSessionData (info, (Process) context.Context);
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
			void method_loaded (ITargetMemoryAccess target, SourceMethod source,
					    object data)
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
