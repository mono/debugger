using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	public class CSharpSymbolHandle : ISymbolHandle
	{
		CSharpSymbolTable symtab;
		ISourceBuffer source;
		LineNumberEntry[] lines;
		MethodEntry method;
		uint end_row;

		internal CSharpSymbolHandle (CSharpSymbolTable symtab, ISourceBuffer source,
					     LineNumberEntry[] lines, uint end_row,
					     MethodEntry method)
		{
			this.symtab = symtab;
			this.source = source;
			this.lines = lines;
			this.end_row = end_row;
			this.method = method;
		}

		public MethodEntry Method {
			get {
				return method;
			}
		}

		public bool IsInSameMethod (ITargetLocation target)
		{
			if (method.Address == null)
				return false;

			ulong address = (ulong) target.Location;

			if ((address < method.Address.StartAddress) ||
			    (address >= method.Address.EndAddress))
				return false;

			return true;
		}

		public ISourceLocation Lookup (ITargetLocation target)
		{
			if (!IsInSameMethod (target))
				return null;

			int source_range, source_offset;
			ulong address = (ulong) target.Location;
			uint row = FindMethodLine (method, lines, address,
						   out source_offset, out source_range);

			if (row == 0) {
				row = end_row;
				source_range = (int) (method.Address.EndAddress - address);
			}

			return new SourceLocation (source, (int) row, source_offset, source_range);
		}

		uint FindMethodLine (MethodEntry method, LineNumberEntry[] LineNumbers, ulong address,
				     out int source_offset, out int source_range)
		{
			source_range = source_offset = 0;
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
					if (next_address == offset)
						continue;
				} else
					source_range = (int) (method.Address.EndAddress - address);

				if (idx > 0)
					source_offset = (int) (offset - method.Address.LineAddresses [idx-1]);
				else
					source_offset = (int) (offset - 1);

				return lne.Row;
			}

			return 0;
		}

		public void Dump ()
		{
			int index = (int) (method.Token & 0xffffff);

			Console.WriteLine ("DUMP: {4} {0} {5} {3} {1} {2}", method,
					   lines.Length, method.Address.LineAddresses.Length,
					   index, symtab.ImageFile, method.Address);

			int count = lines.Length;
			for (int idx = 0; idx < count; idx++) {
				LineNumberEntry lne = lines [idx];
				uint line_address = method.Address.LineAddresses [idx];

				Console.WriteLine ("{0} {1:x} {2:x}", lne, line_address,
						   method.Address.StartAddress + line_address);
			}
		}

		public ITargetLocation Lookup (ISourceLocation source)
		{
			throw new NotImplementedException ();
		}
	}

	public class CSharpSymbolTable : ISymbolTable
	{
		MonoSymbolTableReader symtab;
		ISourceFileFactory source_factory;

		public CSharpSymbolTable (MonoSymbolTableReader symtab, ISourceFileFactory factory)
		{
			this.symtab = symtab;
			this.source_factory = factory;
		}

		internal string ImageFile {
			get {
				return symtab.ImageFile;
			}
		}

		public ISourceLocation Lookup (ITargetLocation target)
		{
			ISymbolHandle handle;

			return Lookup (target, out handle);
		}

		public ISourceLocation Lookup (ITargetLocation target, out ISymbolHandle handle)
		{
			handle = null;

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

				uint EndRow;
				ISourceBuffer source;
				LineNumberEntry[] LineNumbers;
				if (method.SourceFile == null) {
					if (method.Token >> 24 != 6)
						return null;

					int index = (int) (method.Token & 0xffffff);
					Disassembler dis = Disassembler.Disassemble (symtab.ImageFile);
					LineNumbers = dis.GetLines (index);
					EndRow = dis.GetEndLine (index);
					source = dis;
				} else {
					source = source_factory.FindFile (method.SourceFile);
					LineNumbers = method.LineNumbers;
					EndRow = method.EndRow;
				}

				if (source == null)
					return null;

				handle = new CSharpSymbolHandle (this, source, LineNumbers, EndRow, method);
				return handle.Lookup (target);
			}

			return null;
		}

		public ITargetLocation Lookup (ISourceLocation source)
		{
			throw new NotImplementedException ();
		}
	}
}
