using System;
using System.Text;
using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	public class CSharpSymbolTable : ISymbolTable
	{
		MonoSymbolTableReader symtab;
		ISourceFileFactory source_factory;

		public CSharpSymbolTable (MonoSymbolTableReader symtab, ISourceFileFactory factory)
		{
			this.symtab = symtab;
			this.source_factory = factory;
		}

		public ISourceLocation Lookup (ITargetLocation target)
		{
			if (source_factory == null)
				return null;

			long address = target.Location;

			foreach (MethodEntry method in symtab.Methods) {

				if ((address < method.StartAddress) ||
				    (address > method.EndAddress))
					continue;

				ISourceFile source = source_factory.FindFile (method.SourceFile);
				uint row = FindMethodLine (method, address - method.StartAddress, target);

				return new SourceLocation (source, (int) row);
			}

			return null;
		}

		uint FindMethodLine (MethodEntry method, long address, ITargetLocation target)
		{
			int count = method.LineNumbers.Length;
			for (int idx = 0; idx < count; idx++) {
				LineNumberEntry lne = method.LineNumbers [idx];

				Console.WriteLine ("CHECK LINE: {0} {1}", address, lne);

				if (lne.Address < address)
					continue;

				if (idx+1 < count) {
					long next_address = method.LineNumbers [idx+1].Address;
					target.SourceRange = (int) (next_address - address);
				}

				return lne.Row;
			}

			return 0;
		}

		public ITargetLocation Lookup (ISourceLocation source)
		{
			throw new NotImplementedException ();
		}
	}
}
