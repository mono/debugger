using System;

using Mono.Debugger.Backends;

namespace Mono.Debugger
{
	public enum EventType {

		CatchException

	}

	// A general interface to events, like breakpoints, catchpoints..
	// probably watchpoints at some time in the future

	public interface IEventHandle {

		// The breakpoint associated with this event
		Breakpoint Breakpoint { get; }

		// Whether or not the event is active
		bool IsEnabled { get; }

		// Enable the event in the given process
		void Enable (Process process);

		// Disable the event in the given process
		void Disable (Process process);

		// Remove the event from the given process (also
		// disables it)
		void Remove (Process process);
	}
}
