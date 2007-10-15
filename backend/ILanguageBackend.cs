using System;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Backends
{
	internal interface ILanguageBackend : IDisposable
	{
		string Name {
			get;
		}

		Language Language {
			get;
		}
	}
}
