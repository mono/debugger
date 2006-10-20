using System;

namespace Mono.Debugger.Interface
{
	public interface ISymbolLookup
	{
		IMethod Lookup (TargetAddress address);
	}
}
