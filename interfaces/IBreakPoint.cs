using System;

namespace Mono.Debugger
{
	public delegate void BreakPointHandler (IBreakPoint breakpoint);

	/// <summary>
	///   This denotes a breakpoint.
	/// </summary>
	public interface IBreakPoint
	{
		// <summary>
		//   The location in the target's address space.
		// </summary>
		ITargetLocation TargetLocation {
			get;
		}

		// <summary>
		//   This event is hit each time the breakpoint is hit.
		// </summary>
		event BreakPointHandler Hit;

		// <summary>
		//   The number of times this breakpoint has already
		//   been hit.
		// </summary>
		int HitCount {
			get;
		}

		// <summary>
		//   Whether this breakpoint is enabled.  If a
		//   breakpoint is disabled, the application won't
		//   stop when it's hit, but the debugger still knows
		//   about it.
		// </summary>
		bool Enabled {
			get; set;
		}
	}
}
