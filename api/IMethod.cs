using System;

namespace Mono.Debugger.Interface
{
	public interface IMethod : ISymbolLookup, IComparable
	{
		string Name {
			get;
		}

		string ImageFile {
			get;
		}

		bool IsWrapper {
			get;
		}

		bool IsLoaded {
			get;
		}

		bool HasMethodBounds {
			get;
		}

		TargetAddress StartAddress {
			get;
		}

		TargetAddress EndAddress {
			get;
		}

		TargetAddress MethodStartAddress {
			get;
		}

		TargetAddress MethodEndAddress {
			get;
		}

		bool HasSource {
			get;
		}

		IMethodSource Source {
			get;
		}

		ITargetClassType DeclaringType {
			get;
		}

		bool HasThis {
			get;
		}

		ITargetVariable This {
			get;
		}

		ITargetVariable[] Parameters {
			get;
		}

		ITargetVariable[] Locals {
			get;
		}

		ITargetVariable GetVariableByName (TargetAddress address, string name);
	}
}
