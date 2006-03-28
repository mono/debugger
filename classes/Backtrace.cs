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
		int current_frame_idx;

		public Backtrace (StackFrame first_frame)
		{
			this.last_frame = first_frame;

			frames = new ArrayList ();
			frames.Add (first_frame);
		}

		public int Count {
			get { return frames.Count; }
		}

		public StackFrame[] Frames {
			get {
				StackFrame[] retval = new StackFrame [frames.Count];
				frames.CopyTo (retval, 0);
				return retval;
			}
		}

		public StackFrame this [int number] {
			get { return (StackFrame) frames [number]; }
		}

		public StackFrame CurrentFrame {
			get { return this [current_frame_idx]; }
		}

		public int CurrentFrameIndex {
			get { return current_frame_idx; }

			set {
				if ((value < 0) || (value >= frames.Count))
					throw new ArgumentException ();

				current_frame_idx = value;
			}
		}

		public void GetBacktrace (Thread target, Architecture arch,
					  TargetAddress until, int max_frames)
		{
			while (TryUnwind (target, arch, until)) {
				if ((max_frames != -1) && (frames.Count > max_frames))
					break;
			}
		}

		public bool TryUnwind (Thread target, Architecture arch,
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
			new_frame.SetLevel (frames.Count);
			frames.Add (new_frame);
			last_frame = new_frame;
		}
	}
}
