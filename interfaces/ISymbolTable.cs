using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	public interface ISymbolLookup
	{
		ISourceLocation Lookup (ITargetLocation target);

		ITargetLocation Lookup (ISourceLocation source);
	}

	public interface ISymbolContainer
	{
		bool IsContinuous {
			get;
		}

		ITargetLocation StartAddress {
			get;
		}

		ITargetLocation EndAddress {
			get;
		}
	}

	public interface ISymbolTable : ISymbolContainer
	{
		bool Lookup (ITargetLocation target, out IMethod method);

		bool Lookup (ITargetLocation target, out ISourceLocation source, out IMethod method);
	}

	public interface ISymbolTableCollection : ISymbolTable
	{
		void AddSymbolTable (ISymbolTable symtab);
	}
}
