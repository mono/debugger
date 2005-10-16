using System;
using Math = System.Math;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public class Backtrace : MarshalByRefObject
	{
		StackFrame last_frame;
		ArrayList frames;

		public Backtrace (StackFrame first_frame)
		{
			this.last_frame = first_frame;

			frames = new ArrayList ();
			frames.Add (first_frame);
		}

		public StackFrame[] Frames {
			get {
				StackFrame[] retval = new StackFrame [frames.Count];
				frames.CopyTo (retval, 0);
				return retval;
			}
		}

		public void GetBacktrace (TargetAccess target, Architecture arch,
					  TargetAddress until, int max_frames)
		{
			while (TryUnwind (target, arch, until)) {
				if ((max_frames != -1) && (frames.Count > max_frames))
					break;
			}
		}

		public bool TryUnwind (TargetAccess target, Architecture arch,
				       TargetAddress until)
		{
			StackFrame new_frame = null;
			try {
				new_frame = last_frame.UnwindStack (target.TargetMemoryAccess, arch);
			} catch (TargetException) {
			}

			if (new_frame == null)
				return false;

			if (!until.IsNull && (new_frame.StackPointer >= until))
				return false;

			AddFrame (new_frame);
			return true;
		}

		internal void AddFrame (StackFrame new_frame)
		{
			frames.Add (new_frame);
			last_frame = new_frame;
		}
	}
}
