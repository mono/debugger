using System;

namespace Mono.Debugger.Interface
{
	public delegate void DebuggerEventHandler (IDebugger debugger);
	public delegate void ThreadEventHandler (IDebugger debugger, IThread thread);
	public delegate void ProcessEventHandler (IDebugger debugger, IProcess process);

	public interface IDebugger
	{
		event ProcessEventHandler ProcessCreatedEvent;
		event ProcessEventHandler ProcessExitedEvent;
		event ProcessEventHandler ProcessExecdEvent;

		void Kill ();

		void Detach ();

		IProcess Run (IProcessStart start);

		IProcess Attach (IProcessStart start, int pid);

		IProcess[] Processes {
			get;
		}
	}
}
