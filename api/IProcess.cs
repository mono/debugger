using System;

namespace Mono.Debugger.Interface
{
	public interface IProcess
	{
		event TargetOutputHandler TargetOutputEvent;
		event ThreadEventHandler ThreadCreatedEvent;
		event ThreadEventHandler ThreadExitedEvent;
		event DebuggerEventHandler TargetExitedEvent;
		event TargetEventHandler TargetEvent;

		int ID {
			get;
		}

		IDebugger Debugger {
			get;
		}

		IThread MainThread {
			get;
		}

		bool IsManaged {
			get;
		}

		IProcessStart ProcessStart {
			get;
		}

		void Kill ();

		void Detach ();

		IThread[] Threads {
			get;
		}
	}
}
