using System;

namespace Mono.Debugger
{
	public delegate void BreakpointEventHandler (Breakpoint breakpoint);

	[Serializable]
	public abstract class Breakpoint
	{
		public int Index {
			get {
				return index;
			}
		}

		public string Name {
			get {
				return name;
			}
		}

		public bool Enabled {
			get {
				return enabled;
			}

			set {
				enabled = value;
				OnBreakpointChangedEvent ();
			}
		}

		public event BreakpointEventHandler BreakpointChangedEvent;

		// <summary>
		//   This method is called each time the breakpoint is hit.
		//   It returns true if the target should remain stopped and false
		//   if the breakpoint is to be ignored.
		// </summary>
		public abstract bool BreakpointHit (StackFrame frame);

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3})", GetType (), Index, Name, Enabled);
		}

		//
		// Everything below is private.
		//

		protected int index;
		protected string name;
		protected bool enabled;
		static int next_breakpoint_index = 0;

		protected Breakpoint (string name, bool enabled)
		{
			this.index = ++next_breakpoint_index;
			this.enabled = enabled;
			this.name = name;
		}

		protected virtual void OnBreakpointChangedEvent ()
		{
			if (BreakpointChangedEvent != null)
				BreakpointChangedEvent (this);
		}
	}
}
