namespace Mono.Debugger
{
	public interface ISymbolLookup
	{
		ISourceLocation Lookup (ITargetLocation target);

		ITargetLocation Lookup (ISourceLocation source);
	}

	public interface ISymbolHandle : ISymbolLookup
	{
		bool IsInSameMethod (ITargetLocation target);
	}

	public interface ISymbolTable : ISymbolLookup
	{
		ISourceLocation Lookup (ITargetLocation target, out ISymbolHandle handle);
	}
}
