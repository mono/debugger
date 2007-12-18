using System;
using System.IO;

namespace Mono.Debugger
{
	public delegate void TargetEventHandler (Thread thread, TargetEventArgs args);

	[Serializable]
	public enum TargetEventType
	{
		TargetRunning,
		TargetStopped,
		TargetInterrupted,
		TargetHitBreakpoint,
		TargetSignaled,
		TargetExited,
		FrameChanged,
		Exception,
		UnhandledException
	}

	[Serializable]
	public class TargetEventArgs
	{
		public readonly TargetEventType Type;
		public readonly object Data;
		public readonly StackFrame Frame;

		public TargetEventArgs (TargetEventType type)
		{
			this.Type = type;
		}

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
