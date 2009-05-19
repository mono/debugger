using System;
using Math = System.Math;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;

namespace Mono.Debugger
{
	public class Backtrace : DebuggerMarshalByRefObject
	{
		public enum Mode {
			Default,
			Native,
			Managed
		}

		StackFrame last_frame;
		ArrayList frames;
		int current_frame_idx;

		bool tried_lmf;
		TargetAddress lmf_address;

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

		internal void GetBacktrace (ThreadServant thread, TargetMemoryAccess memory,
					    Mode mode, TargetAddress until, int max_frames)
		{
			while (TryUnwind (thread, memory, mode, until)) {
				if ((max_frames != -1) && (frames.Count > max_frames))
					break;
			}

			// Ugly hack: in Mode == Mode.Default, we accept wrappers but not as the
			//            last frame.
			if ((mode == Mode.Default) && (frames.Count > 1)) {
				StackFrame last = this [frames.Count - 1];
				if (!IsFrameOkForMode (last, Mode.Managed))
					frames.Remove (last);
			}
		}

		private StackFrame TryLMF (ThreadServant thread, TargetMemoryAccess memory)
		{
			try {
				if (lmf_address.IsNull)
					return null;

				StackFrame new_frame = thread.Architecture.GetLMF (thread, memory, ref lmf_address);
				if (new_frame == null)
					return null;

				// Sanity check; don't loop.
				if (new_frame.StackPointer <= last_frame.StackPointer)
					return null;

				return new_frame;
			} catch (TargetException) {
				return null;
			}
		}

		private bool TryCallback (ThreadServant thread, TargetMemoryAccess memory,
					  StackFrame last_frame, bool exact_match)
		{
			try {
				Inferior.CallbackFrame callback = thread.GetCallbackFrame (
					last_frame.StackPointer, exact_match);
				if (callback == null)
					return false;

				StackFrame frame = thread.Architecture.CreateFrame (
					thread.Client, FrameType.Normal, memory, callback.Registers);

				FrameType callback_type = callback.IsRuntimeInvokeFrame ? FrameType.RuntimeInvoke : FrameType.Callback;
				AddFrame (new StackFrame (
					thread.Client, callback_type, callback.CallAddress, callback.StackPointer,
					TargetAddress.Null, callback.Registers, thread.NativeLanguage,
					new Symbol ("<method called from mdb>", callback.CallAddress, 0)));
				AddFrame (frame);
				return true;
			} catch (TargetException) {
				return false;
			}
		}

		private bool IsFrameOkForMode (StackFrame frame, Mode mode)
		{
			if (mode == Mode.Native)
				return true;
			if ((frame.Language == null) || !frame.Language.IsManaged)
				return false;
			if (mode == Mode.Default)
				return true;
			if ((frame.SourceAddress == null) || (frame.Method == null))
				return false;
			return frame.Method.WrapperType == WrapperType.None;
		}

		internal bool TryUnwind (ThreadServant thread, TargetMemoryAccess memory,
					 Mode mode, TargetAddress until)
		{
			StackFrame new_frame = null;
			try {
				new_frame = last_frame.UnwindStack (memory);
			} catch (TargetException) {
			}

			if ((new_frame == null) || !IsFrameOkForMode (new_frame, mode)) {
				if (TryCallback (thread, memory, last_frame, false))
					return true;

				if (!tried_lmf) {
					tried_lmf = true;
					if (thread.LMFAddress.IsNull)
						return false;
					lmf_address = memory.ReadAddress (thread.LMFAddress);
				}

				if (!lmf_address.IsNull)
					new_frame = TryLMF (thread, memory);
				else
					return false;
			} else if (TryCallback (thread, memory, new_frame, true))
				return true;

			if (new_frame == null)
				return false;

			// Sanity check; don't loop.
			if (new_frame.StackPointer <= last_frame.StackPointer)
				return false;

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
