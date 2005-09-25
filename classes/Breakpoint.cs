using System;

namespace Mono.Debugger
{
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
		// </summary>
		// <remarks>
		//   The @target argument is *not* serializable and may not be used
		//   anywhere outside this handler.
		// </remarks>
		public virtual bool CheckBreakpointHit (TargetAccess target, TargetAddress address)
		{
			return true;
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2})", GetType (), Index, Name);
		}

		//
		// Everything below is protected.
		//

		protected int index;
		protected string name;
		protected ThreadGroup group;

		protected static int NextBreakpointIndex = 0;

		protected Breakpoint (string name, ThreadGroup group)
		{
			this.index = ++NextBreakpointIndex;
			this.group = group;
			this.name = name;
		}
	}
}
