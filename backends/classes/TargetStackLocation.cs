using System;
using System.Text;
using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	internal class TargetStackLocation : TargetLocation
	{
		DebuggerBackend backend;
		StackFrame frame;
		IInferiorStackFrame iframe;
		TargetAddress start_scope, end_scope;
		bool is_local;

		internal TargetStackLocation (DebuggerBackend backend, StackFrame frame,
					      bool is_local, long offset, TargetAddress start_scope,
					      TargetAddress end_scope)
			: base (offset)
		{
			this.backend = backend;
			this.is_local = is_local;
			this.start_scope = start_scope;
			this.end_scope = end_scope;
			this.frame = frame;

			frame.FrameInvalid += new StackFrameInvalidHandler (FrameInvalidEvent);
			iframe = frame.Handle as IInferiorStackFrame;
			if (iframe == null)
				throw new InternalError ();

			backend.FramesInvalidEvent += new StackFrameInvalidHandler (FrameInvalidEvent);
		}

		void FrameInvalidEvent ()
		{
			SetInvalid ();
		}

		protected override object GetHandle ()
		{
			return frame;
		}

		protected override TargetAddress GetAddress ()
		{
			if (is_local)
				return new TargetAddress (frame, iframe.LocalsAddress.Address + Offset);
			else
				return new TargetAddress (frame, iframe.ParamsAddress.Address + Offset);
		}

		protected override bool GetIsValid ()
		{
			if ((frame.TargetAddress < start_scope) || (frame.TargetAddress >= end_scope))
				return false;

			return true;
		}

		public override object Clone ()
		{
			return new TargetStackLocation (backend, frame, is_local, Offset,
							start_scope, end_scope);
		}
	}
}
