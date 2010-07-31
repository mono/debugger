using System;
using System.Xml;

using Mono.Debugger.Backend;
using Mono.Debugger.Backend.Mono;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Native;
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

		internal override BreakpointHandle Resolve (Thread target, StackFrame frame)
		{
			if (handle != null)
				return handle;

			if (frame.Thread.Process.IsManaged) {
				MonoLanguageBackend mono = frame.Thread.Process.MonoLanguage;
				MonoFunctionType main = mono.MainMethod;
				if (main == null)
					return null;

				handle = main.GetBreakpointHandle (this, -1, -1);
			} else {
				OperatingSystemBackend os = frame.Thread.Process.OperatingSystem;
				TargetAddress main = os.LookupSymbol ("main");
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

		public override bool CheckBreakpointHit (Thread target, TargetAddress address)
		{
			return target.Process.ProcessStart.StopInMain;
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
