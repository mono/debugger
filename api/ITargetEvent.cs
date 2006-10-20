using System;

namespace Mono.Debugger.Interface
{
	public delegate void TargetEventHandler (IThread thread, ITargetEventArgs args);

	[Serializable]
	public enum TargetEventType
	{
		TargetRunning,
		TargetStopped,
		TargetHitBreakpoint,
		TargetSignaled,
		TargetExited,
		Exception,
		UnhandledException
	}

	public interface ITargetEventArgs
	{
		TargetEventType Type {
			get;
		}

		object Data {
			get;
		}

		IStackFrame Frame {
			get;
		}
	}
}
