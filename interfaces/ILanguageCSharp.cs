using System;
using System.Reflection;

namespace Mono.Debugger.Languages
{
	// <summary>
	//   The C# programming language.
	// </summary>
	public interface ILanguageCSharp : ISourceLanguage
	{
		// <summary>
		//   Create a target location for the specified method.
		// </summary>
		ITargetLocation CreateLocation (MethodInfo method);

		// <summary>
		//   Get the current assembly.
		// </summary>
		Assembly CurrentAssembly {
			get;
		}
	}
}
