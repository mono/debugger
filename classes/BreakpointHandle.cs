using System;
using System.Runtime.Serialization;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	internal sealed class BreakpointHandle : EventHandle
	{
		Breakpoint breakpoint;
		SourceLocation location;
		TargetFunctionType function;
		int breakpoint_id = -1;
		ILoadHandler load_handler;
		int domain;

		private BreakpointHandle (Breakpoint breakpoint)
			: base (breakpoint.ThreadGroup, breakpoint.Name, breakpoint.Index)
		{
			this.breakpoint = breakpoint;
		}

		internal BreakpointHandle (Breakpoint breakpoint, int domain, SourceLocation location)
			: this (breakpoint)
		{
			this.domain = domain;
			this.location = location;
		}

		internal BreakpointHandle (Breakpoint breakpoint, TargetFunctionType func)
			: this (breakpoint)
		{
			this.function = func;
		}

		internal BreakpointHandle (Breakpoint breakpoint, int breakpoint_id)
			: this (breakpoint)
		{
			this.breakpoint_id = breakpoint_id;
		}

		public override bool IsEnabled {
			get { return (breakpoint_id > 0) || (load_handler != null); }
		}

		public override void Enable (Thread target)
		{
			lock (this) {
				EnableBreakpoint (target);
			}
		}

		public override void Disable (Thread target)
		{
			lock (this) {
				DisableBreakpoint (target);
			}
		}

		public override void Remove (Thread target)
		{
			if (load_handler != null) {
				load_handler.Remove ();
				load_handler = null;
			}
			Disable (target);
		}

		public override bool CheckBreakpointHit (Thread target, TargetAddress address)
		{
			return breakpoint.CheckBreakpointHit (target, address);
		}

		void EnableBreakpoint (Thread target)
		{
			if ((load_handler != null) || (breakpoint_id > 0))
				return;

			if (location != null) {
				TargetAddress address = location.GetAddress (domain);
				if (!address.IsNull)
					breakpoint_id = target.InsertBreakpoint (breakpoint, address);
				else if (location.Method.IsDynamic) {
					// A dynamic method is a method which may emit a
					// callback when it's loaded.  We register this
					// callback here and do the actual insertion when
					// the method is loaded.
					load_handler = location.Module.RegisterLoadHandler (
						target, location.Method, method_loaded, null);
				}
			} else if (function != null) {
				if (function.IsLoaded)
					breakpoint_id = target.InsertBreakpoint (breakpoint, function);
				else
					load_handler = function.Module.RegisterLoadHandler (
						target, function.Source, method_loaded, null);
			}
		}

		void DisableBreakpoint (Thread target)
		{
			if (breakpoint_id > 0)
				target.RemoveBreakpoint (breakpoint_id);

			if (load_handler != null)
				load_handler.Remove ();

			load_handler = null;
			breakpoint_id = -1;
		}

		public SourceLocation SourceLocation {
			get { return location; }
		}

		// <summary>
		//   The method has just been loaded, lookup the breakpoint
		//   address and actually insert it.
		// </summary>
		void method_loaded (ITargetMemoryAccess target, SourceMethod source,
				    object data)
		{
			load_handler = null;

			Method method = source.GetMethod (domain);
			if (method == null)
				return;

			TargetAddress address;
			if (location != null)
				address = location.GetAddress (domain);
			else
				address = method.StartAddress;

			if (address.IsNull)
				return;

			breakpoint_id = target.InsertBreakpoint (breakpoint, address);
		}

		protected override void GetSessionData (SerializationInfo info)
		{
			base.GetSessionData (info);
			info.AddValue ("breakpoint", breakpoint);
			info.AddValue ("domain", domain);
			if (location != null) {
				info.AddValue ("type", "location");
				info.AddValue ("location", location);
			} else {
				info.AddValue ("type", "function");
				info.AddValue ("function", function.Name);
			}
		}

		protected override void SetSessionData (SerializationInfo info, Process process)
		{
			base.SetSessionData (info, process);
			domain = (int) info.GetInt32 ("domain");
			breakpoint = (Breakpoint) info.GetValue ("breakpoint", typeof (Breakpoint));

			string type = info.GetString ("type");
			if (type == "location")
				location = (SourceLocation) info.GetValue (
					"location", typeof (SourceLocation));
			else if (type == "function") {
				Language language = process.MonoLanguage;
				string funcname = info.GetString ("function");
				function = (TargetFunctionType) language.LookupType (funcname);
			}
		}
	}
}
