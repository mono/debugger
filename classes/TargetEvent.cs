using System;
using System.IO;

namespace Mono.Debugger
{
	public delegate void TargetEventHandler (object sender, TargetEventArgs args);

	public enum TargetEventType
	{
		TargetRunning,
		TargetStopped,
		TargetHitBreakpoint,
		TargetSignaled,
		TargetExited,
		FrameChanged
	}

	public class TargetEventArgs
	{
		public readonly TargetEventType Type;
		public readonly object Data;
		public readonly StackFrame Frame;

		public TargetEventArgs (TargetEventType type, object data)
		{
			this.Type = type;
			this.Data = data;
		}

		public TargetEventArgs (TargetEventType type, object data, StackFrame frame)
			: this (type, data)
		{
			this.Frame = frame;
		}

		public TargetEventArgs (TargetEventType type, StackFrame frame)
			: this (type, (object) null)
		{
			this.Frame = frame;
		}

		public bool IsStopped {
			get { return Type == TargetEventType.TargetStopped ||
				      Type == TargetEventType.TargetHitBreakpoint; }
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3})", GetType (), Type, Data, Frame);
		}
	}
}
