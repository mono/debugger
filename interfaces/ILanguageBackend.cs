using System;

namespace Mono.Debugger
{
	public interface ILanguageBackend : ISymbolTable
	{
		void UpdateSymbolTable ();
	}
}

