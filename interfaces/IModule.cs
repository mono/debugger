using System;

namespace Mono.Debugger
{
	public delegate void ModulesChangedHandler ();

	public interface IModule
	{
		ILanguageBackend Language {
			get;
		}

		string Name {
			get;
		}

		string FullName {
			get;
		}

		// <summary>
		//   Whether the module is current loaded.
		// </summary>
		bool IsLoaded {
			get;
		}

		// <summary>
		//   Whether the module's symbol tables are loaded.  This is also true if the
		//   symbol tables are only partitially loaded or if they're loaded on demand.
		// </summary>
		bool SymbolsLoaded {
			get;
		}

		// <summary>
		//   This property is set by the user interface.
		//   If true, the debugger will load the module's symbol tables and display
		//   source code if the target stops somewhere in this module.
		// </summary>
		// <remarks>
		//   This property only specifies whether to display source code for a method
		//   if you're already in that method.  It does not influence whether to enter
		//   a method while single-stepping.
		// </remarks>
		bool LoadSymbols {
			get; set;
		}

		// <summary>
		//   This property is set by the user interface.
		//   If false, the debugger will not enter any of this module's methods when
		//   single-stepping.
		// </summary>
		// <remarks>
		//   This property only specifies whether to enter a method when single-stepping,
		//   not whether to display source code if you're already in that method.
		// </remarks>
		bool StepInto {
			get; set;
		}
	}
}
