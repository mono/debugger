using System;

namespace Mono.Debugger
{
	public delegate void BreakpointEventHandler (Breakpoint breakpoint);

	// <summary>
	//   This is an abstract base class which is implemented by the user interface to
	//   hold the user's settings for a breakpoint.
	// </summary>

	[Serializable]
	public abstract class Breakpoint
	{
		// <summary>
		//   An automatically generated unique index for this breakpoint.
		// </summary>
		// <remarks>
		//   Do not restore this from a serialized memory stream, but increment
		//   the protected static variable `NextBreakpointIndex' to get an unique
		//   number.
		// </remarks>
		public int Index {
			get {
				return index;
			}
		}

		// <summary>
		//   The breakpoint's name.  This property has no meaning at all for the
		//   backend, it's just something which can be displayed to the user to
		//   help him indentify this breakpoint.
		// </summary>
		public string Name {
			get {
				return name;
			}

			set {
				name = value;
			}
		}

		// <summary>
		//   Whether this breakpoint is currently enabled.
		// </summary>
		public bool Enabled {
			get {
				return enabled;
			}

			set {
				enabled = value;
				OnBreakpointChangedEvent ();
			}
		}

		// <summary>
		//   This event is emitted each time the `Enabled' property is changed.
		//   The backend listens to it to actually enable/disable the breakpoint,
		//   but it can also be used by the user interface to check/uncheck an
		//   `enabled' checkbox, for instance.
		// </summary>
		public event BreakpointEventHandler BreakpointChangedEvent;

		// <summary>
		//   Whether the `BreakpointHit' delegate needs the StackFrame argument.
		//   Constructing this argument is an expensive operation, so you should
		//   set it to false unless your handler actually needs it.  Normally,
		//   your handler only needs this argument if it wants to access any
		//   parameters or local variables.
		// </summary>
		public bool HandlerNeedsFrame {
			get {
				return needs_frame;
			}

			set {
				needs_frame = value;
			}
		}

		// <summary>
		//   This method is called each time the breakpoint is hit.
		//   It returns true if the target should remain stopped and false
		//   if the breakpoint is to be ignored.
		//   The @frame argument is only computed if the `HandlerNeedsFrame'
		//   property is true, otherwise it's set to null.
		// </summary>
		// <remarks>
		//   This delegate is invoked _before_ any notifications are sent, so you
		//   must not attempt to access the CurrentFrame or the CurrentMethod in
		//   this handler.  If you want to inspect local variables, or parameters,
		//   set the `HandlerNeedsFrame' property to true and use the @frame
		//   argument which is passed to you.
		//   The reason for this behavior is that these notifications won't be
		//   sent at all (and thus the CurrentFrame and CurrentMethod won't even
		//   get computed) if the target is to be continued.  This is necessary to
		//   get a flicker-free UI if you have a breakpoint which is ignored the
		//   first 1000 times it is hit, for instance.
		// </remarks>
		public virtual bool CheckBreakpointHit (StackFrame frame)
		{
			return true;
		}

		public abstract void BreakpointHit (StackFrame frame);

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3})", GetType (), Index, Name, Enabled);
		}

		//
		// Everything below is protected.
		//

		protected int index;
		protected string name;
		protected bool enabled;
		protected bool needs_frame;

		protected static int NextBreakpointIndex = 0;

		protected Breakpoint (string name, bool enabled, bool needs_frame)
		{
			this.index = ++NextBreakpointIndex;
			this.enabled = enabled;
			this.needs_frame = needs_frame;
			this.name = name;
		}

		protected virtual void OnBreakpointChangedEvent ()
		{
			if (BreakpointChangedEvent != null)
				BreakpointChangedEvent (this);
		}
	}
}
