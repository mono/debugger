using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
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

			ulong address = (ulong) target.Location;

			foreach (MethodEntry method in symtab.Methods) {

				if (method.Address == null)
					continue;

				MethodAddress method_address = method.Address;

				if ((address < method_address.StartAddress) ||
				    (address >= method_address.EndAddress))
					continue;

				ISourceBuffer source;
				LineNumberEntry[] LineNumbers;
				if (method.SourceFile == null) {
					if (method.Token >> 24 != 6)
						return null;

					int index = (int) (method.Token & 0xffffff);
					Disassembler dis = Disassembler.Disassemble (symtab.ImageFile);
					LineNumbers = dis.GetLines (index);
					source = dis;
				} else {
					source = source_factory.FindFile (method.SourceFile);
					LineNumbers = method.LineNumbers;
				}

				if (source == null)
					return null;

				int source_range;
				uint row = FindMethodLine (method, LineNumbers, address, out source_range);

				return new SourceLocation (source, (int) row, source_range);
			}

			return null;
		}

		uint FindMethodLine (MethodEntry method, LineNumberEntry[] LineNumbers, ulong address,
				     out int source_range)
		{
			source_range = 0;
			int count = LineNumbers.Length;
			uint offset = (uint) (address - method.Address.StartAddress);

			for (int idx = 0; idx < count; idx++) {
				LineNumberEntry lne = LineNumbers [idx];
				uint line_address = method.Address.LineAddresses [idx];

				if ((offset > 1) && (line_address < offset))
					continue;

				if (idx+1 < count) {
					uint next_address = method.Address.LineAddresses [idx+1];
					source_range = (int) (next_address - offset);
					if (next_address == line_address)
						continue;
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
