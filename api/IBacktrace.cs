using System;

namespace Mono.Debugger.Interface
{
	public interface IBacktrace
	{
		int Count {
			get;
		}

		int CurrentFrameIndex {
			get;
			set;
		}

		IStackFrame[] Frames {
			get;
		}

		IStackFrame CurrentFrame {
			get;
		}
	}
}
