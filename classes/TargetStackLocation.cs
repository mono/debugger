using System;
using System.Text;

namespace Mono.Debugger
{
	public class TargetStackLocation : TargetLocation
	{
		IDebuggerBackend backend;
		IStackFrame frame;
		IInferiorStackFrame iframe;
		TargetAddress start_scope, end_scope;
		bool is_local;

		public TargetStackLocation (IDebuggerBackend backend, IStackFrame frame,
					    bool is_local, long offset, TargetAddress start_scope,
					    TargetAddress end_scope)
			: base (offset)
		{
			this.backend = backend;
			this.is_local = is_local;
			this.start_scope = start_scope;
			this.end_scope = end_scope;

			set_frame (frame);

			backend.FrameChangedEvent += new StackFrameHandler (FrameChangedEvent);
			backend.FramesInvalidEvent += new StackFrameInvalidHandler (FramesInvalidEvent);
		}

		void set_frame (IStackFrame new_frame)
		{
			frame = new_frame;
			frame.FrameInvalid += new StackFrameInvalidHandler (FramesInvalidEvent);
			iframe = frame.FrameHandle as IInferiorStackFrame;
			if (iframe == null)
				throw new InternalError ();
		}

		void FramesInvalidEvent ()
		{
			is_valid = false;
		}

		void FrameChangedEvent (IStackFrame frame)
		{
			if (!is_valid)
				return;
			if ((frame.TargetAddress < start_scope) || (frame.TargetAddress >= end_scope))
				is_valid = false;
		}

		public override TargetAddress GetAddress ()
		{
			if (is_local)
				return new TargetAddress (frame, iframe.LocalsAddress.Address + Offset);
			else
				return new TargetAddress (frame, iframe.ParamsAddress.Address + Offset);
		}

		public override bool ReValidate ()
		{
			if (!backend.HasTarget)
				return false;

			IStackFrame[] backtrace = backend.GetBacktrace ();

			foreach (IStackFrame frame in backtrace) {
				TargetAddress address = frame.TargetAddress;

				if ((address >= start_scope) && (address < end_scope)) {
					set_frame (frame);
					return true;
				}
			}

			return false;
		}

		public override object Clone ()
		{
			return new TargetStackLocation (backend, frame, is_local, Offset,
							start_scope, end_scope);
		}
	}
}
