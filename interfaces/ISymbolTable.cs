namespace Mono.Debugger
{
	public interface ISymbolTable
	{
		ISourceLocation Lookup (ITargetLocation target);

		ITargetLocation Lookup (ISourceLocation source);
	}
}
