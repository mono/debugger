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

		bool IsLoaded {
			get;
		}

		bool SymbolsLoaded {
			get;
		}

		bool LoadSymbols {
			get; set;
		}
	}
}
