using System;

namespace Mono.Debugger.Languages.Native
{
	internal interface ITypeEntry
	{
		string Name {
			get;
		}

		bool IsComplete {
			get;
		}

		TargetType ResolveType ();
	}
}
