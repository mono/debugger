using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	public interface ISymbolLookup
	{
		ISourceLocation Lookup (ITargetLocation target);

		ITargetLocation Lookup (ISourceLocation source);
	}

	public interface ISymbolTable : ISymbolLookup
	{
		ISourceLocation Lookup (ITargetLocation target, out IMethod method);
	}
}
