using System;
using System.Collections;
using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	public class CSharpMethod : IMethod
	{
		CSharpSymbolTable symtab;
		CSharpMethodSource source;
		MethodEntry method;

		protected class CSharpMethodSource : IMethodSource
		{
			int start_row, end_row;
			ISourceBuffer source;
			LineNumberEntry[] LineNumbers;
			MethodEntry method;

			public CSharpMethodSource (ISourceBuffer source, MethodEntry method,
						   int start_row, int end_row,
						   LineNumberEntry[] LineNumbers)
			{
				this.source = source;
				this.method = method;
				this.start_row = start_row;
				this.end_row = end_row;
				this.LineNumbers = LineNumbers;
			}

			public ISourceBuffer SourceBuffer {
				get {
					return source;
				}
			}

			public int StartRow {
				get {
					return start_row;
				}
			}

			public int EndRow {
				get {
					return end_row;
				}
			}

			internal int FindMethodLine (ulong address, out int source_offset,
						     out int source_range)
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
					} else {
						source_range = (int) (method.Address.EndAddress - address);
					}

					if (idx > 0)
						source_offset = (int) (offset - method.Address.LineAddresses [idx-1]);
					else
						source_offset = (int) (offset - 1);

					return (int) lne.Row;
				}

				return 0;
			}

			internal void Dump ()
			{
				int index = (int) (method.Token & 0xffffff);

				Console.WriteLine ("DUMP: {4} {0} {5} {3} {1} {2}", method,
						   LineNumbers.Length, method.Address.LineAddresses.Length,
						   index, symtab.ImageFile, method.Address);

				int count = LineNumbers.Length;
				for (int idx = 0; idx < count; idx++) {
					LineNumberEntry lne = LineNumbers [idx];
					uint line_address = method.Address.LineAddresses [idx];

					Console.WriteLine ("{0} {1:x} {2:x}", lne, line_address,
							   method.Address.StartAddress + line_address);
				}
			}
		}

		public CSharpMethod (CSharpSymbolTable symtab, MethodEntry method)
		{
			this.symtab = symtab;
			this.method = method;

			if (method.SourceFile == null) {
				if (method.Token >> 24 == 6) {
					int index = (int) (method.Token & 0xffffff);
					ILDisassembler dis = ILDisassembler.Disassemble (symtab.ImageFile);

					source = new CSharpMethodSource (
						dis, method, dis.GetStartLine (index), dis.GetEndLine (index),
						dis.GetLines (index));
				}
			} else {
				ISourceBuffer buffer = symtab.SourceFactory.FindFile (method.SourceFile);
				if (buffer != null)
					source = new CSharpMethodSource (
						buffer, method, (int) method.StartRow, (int) method.EndRow,
						method.LineNumbers);
			}
		}

		//
		// IMethod
		//

		public string ImageFile {
			get {
				return symtab.ImageFile;
			}
		}

		public object MethodHandle {
			get {
				return (long) method.Token;
			}
		}

		public IMethodSource Source {
			get {
				return source;
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
			if (!IsInSameMethod (target) || (source == null))
				return null;

			int source_range, source_offset;
			int row = source.FindMethodLine
				((ulong) target.Address, out source_offset, out source_range);

			if (row == 0) {
				row = source.EndRow;
				source_range = (int) ((long) method.Address.EndAddress - target.Address);
			}

			return new SourceLocation (source.SourceBuffer, row, source_offset, source_range);
		}

		public ITargetLocation StartAddress {
			get {
				return new TargetLocation ((long) method.Address.StartAddress);
			}
		}

		public ITargetLocation EndAddress {
			get {
				return new TargetLocation ((long) method.Address.EndAddress);
			}
		}
	}
}
