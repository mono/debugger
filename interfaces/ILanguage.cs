using System;

namespace Mono.Debugger
{
	// <summary>
	//   This denotes a programming language which can be debugged
	//   by the current debugger backend.
	// </summary>
	public interface ISourceLanguage
	{
		// <summary>
		//   Create a target location for the `main' method.
		// </summary>
		ITargetLocation MainLocation {
			get;
		}
	}
}
