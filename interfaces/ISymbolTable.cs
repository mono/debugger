using System;
using System.Collections;
using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	public interface ISymbolLookup
	{
		IMethod Lookup (TargetAddress address);

		ISymbol Lookup (string name);
	}

	public interface ISymbolContainer
	{
		// <summary>
		//   StartAddress and EndAddress are only valid if this is true.
		// </summary>
		bool IsContinuous {
			get;
		}

		TargetAddress StartAddress {
			get;
		}

		TargetAddress EndAddress {
			get;
		}
	}

	public interface ISymbolRange : IComparable
	{
		TargetAddress StartAddress {
			get;
		}

		TargetAddress EndAddress {
			get;
		}

		// <summary>
		//   If the address you're looking for is within the
		//   [StartAddress,EndAddress] interface, use this property
		//   to get an ISymbolLookup instance which you can use to
		//   search the symbol.  This'll automatically load the
		//   symbol table from disk if necessary.
		// </summary>
		ISymbolLookup SymbolLookup {
			get;
		}
	}

	public interface ISymbol : IComparable
	{
		string Name {
			get;
		}

		ITargetLocation Location {
			get;
		}
	}

	public delegate void SymbolTableChangedHandler ();

	public interface ISymbolTable : ISymbolLookup, ISymbolContainer
	{
		// <summary>
		//   Whether this symbol table has an address range table.
		// </summary>
		bool HasRanges {
			get;
		}

		// <summary>
		//   If HasRanges is true, this is a sorted (by start address)
		//   list of address ranges.  To lookup a symbol by its address,
		//   first search in this table to find the correct ISymbolRange,
		//   then use its SymbolLookup property to get an ISymbolLookup
		//   on which you can do a Lookup().
		// </summary>
		// <remarks>
		//   When searching in more than one ISymbolTable, consider using
		//   an ISymbolTableCollection instead since this will merge the
		//   address ranges from all its symbol tables into one big table
		//   and thus a lookup fill me faster.
		// </remarks>
		ISymbolRange[] SymbolRanges {
			get;
		}

		// <summary>
		//   Whether the symbol table has a symbol list.
		// </summary>
		bool HasSymbols {
			get;
		}

		// <summary>
		//   If HasNames is true, returns a list of ISymbols which can
		//   be used to lookup symbols by name.
		// </summary>
		ISymbol[] Symbols {
			get;
		}

		bool IsLoaded {
			get;
		}

		void UpdateSymbolTable ();

		event SymbolTableChangedHandler SymbolTableChanged;
	}
}
