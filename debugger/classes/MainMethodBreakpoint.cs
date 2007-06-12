using System;
using System.Xml;
using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger
{
	public class MainMethodBreakpoint : Breakpoint
	{
		public readonly DebuggerSession Session;
		BreakpointHandle handle;

		public override bool IsPersistent {
			get { return true; }
		}

		public override bool IsActivated {
			get { return handle != null; }
		}

		public override bool HideFromUser {
			get { return true; }
		}

		internal override BreakpointHandle Resolve (TargetMemoryAccess target, StackFrame frame)
		{
			if (handle != null)
				return handle;

			if (frame.Thread.Process.IsManaged) {
				MonoLanguageBackend mono = frame.Thread.Process.Servant.MonoLanguage;
				MonoFunctionType main = mono.MainMethod;
				if (main == null)
					return null;

				handle = new FunctionBreakpointHandle (this, 0, main, -1);
			} else {
				BfdContainer bfd_container = frame.Thread.Process.Servant.BfdContainer;
				TargetAddress main = bfd_container.LookupSymbol ("main");
				if (main.IsNull)
					return null;

				handle = new AddressBreakpointHandle (this, main);
			}

			return handle;
		}

		public override void Activate (Thread target)
		{
			Resolve (target, target.CurrentFrame);
			if (handle == null)
				throw new TargetException (TargetError.LocationInvalid);
			handle.Insert (target);
		}

		public override void Deactivate (Thread target)
		{
			if (handle != null) {
				handle.Remove (target);
				handle = null;
			}
		}

		internal override void OnTargetExited ()
		{
			handle = null;
		}

		protected override void GetSessionData (XmlElement root, XmlElement element)
		{
			XmlElement location_e = root.OwnerDocument.CreateElement ("MainMethod");
			element.AppendChild (location_e);
		}

		internal MainMethodBreakpoint (DebuggerSession session)
			: base (EventType.Breakpoint, "<main>", ThreadGroup.Global)
		{
			this.Session = session;
		}
	}
}
