using System;
using Math = System.Math;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public class Backtrace : DebuggerMarshalByRefObject
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

		internal void GetBacktrace (ThreadServant target, TargetAddress until,
					    int max_frames)
		{
			while (TryUnwind (target, until)) {
				if ((max_frames != -1) && (frames.Count > max_frames))
					break;
			}
		}

		internal bool TryUnwind (ThreadServant target, TargetAddress until)
		{
			StackFrame new_frame = null;
			try {
				new_frame = last_frame.UnwindStack (target);
			} catch (TargetException) {
			}

#if TEST_ME
			if ((new_frame == null) || (new_frame.SourceAddress == null)) {
				try {
					if (!last_frame.Language.IsManaged && !target.LMFAddress.IsNull)
						new_frame = target.Architecture.GetLMF (target.Client);
				} catch (TargetException) {
				}

				if (new_frame == null)
					return false;

				// Sanity check; don't loop.
				if (new_frame.StackPointer < last_frame.StackPointer)
					return false;
			}
#else
			if (new_frame == null)
				return false;
#endif

			if (!until.IsNull && (new_frame.StackPointer >= until))
				return false;

			AddFrame (new_frame);
			return true;
		}

		public string Print ()
		{
			StringBuilder sb = new StringBuilder ();
			foreach (StackFrame frame in frames)
				sb.Append (String.Format ("{0}\n", frame));
			return sb.ToString ();
		}

		internal void AddFrame (StackFrame new_frame)
		{
			new_frame.SetLevel (frames.Count);
			frames.Add (new_frame);
			last_frame = new_frame;
		}
	}
}
