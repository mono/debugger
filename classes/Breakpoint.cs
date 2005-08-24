using System;

namespace Mono.Debugger
{
	public delegate void BreakpointEventHandler (Breakpoint breakpoint, StackFrame frame);

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
		//   The ThreadGroup in which this breakpoint "breaks".
		//   If null, then it breaks in all threads.
		// </summary>
		public ThreadGroup ThreadGroup {
			get {
				return group;
			}

			set {
				group = value;
			}
		}

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

		public bool Breaks (int id)
		{
			if ((group == null) || group.IsGlobal)
				return true;

			foreach (int thread in group.Threads) {
				if (thread == id)
					return true;
			}

			return false;
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
		public virtual bool CheckBreakpointHit (TargetAddress frame_address, StackFrame frame,
							ITargetAccess target)
		{
			return true;
		}

		public abstract void BreakpointHit (StackFrame frame);

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2})", GetType (), Index, Name);
		}

		//
		// Everything below is protected.
		//

		protected int index;
		protected string name;
		protected bool needs_frame;
		protected ThreadGroup group;

		protected static int NextBreakpointIndex = 0;

		protected Breakpoint (string name, ThreadGroup group, bool needs_frame)
		{
			this.index = ++NextBreakpointIndex;
			this.needs_frame = needs_frame;
			this.group = group;
			this.name = name;
		}
	}
}
