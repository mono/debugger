using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public struct LineEntry : IComparable {
		public readonly long Address;
		public readonly int Line;

		public LineEntry (long address, int line)
		{
			this.Address = address;;
			this.Line = line;
		}

		public int CompareTo (object obj)
		{
			LineEntry entry = (LineEntry) obj;

			if (entry.Address < Address)
				return 1;
			else if (entry.Address > Address)
				return -1;
			else
				return 0;
		}
	}

	public class NativeMethod : IMethodSource, IMethod
	{
		IDisassembler disassembler;
		ITargetLocation start, end;
		ArrayList addresses;
		WeakReference weak_source;
		int start_row, end_row;
		string image_file;
		string name;

		public NativeMethod (IDisassembler dis, IMethod method)
			: this (dis, method.Name, method.ImageFile,
				method.StartAddress, method.EndAddress)
		{ }

		public NativeMethod (IDisassembler dis, string name, string image_file,
				     ITargetLocation start, ITargetLocation end)
			: this (name, image_file, start, end)
		{
			this.disassembler = dis;
		}

		public NativeMethod (string name, string image_file,
				     ITargetLocation start, ITargetLocation end)
		{
			this.name = name;
			this.image_file = image_file;
			this.start = start;
			this.end = end;
		}

		ISourceBuffer ReadSource ()
		{
			ISourceBuffer source = null;
			if (weak_source != null) {
				try {
					source = (ISourceBuffer) weak_source.Target;
				} catch {
					weak_source = null;
				}
			}

			if (source != null)
				return source;

			source = ReadSource (out start_row, out end_row, out addresses);
			if (source != null)
				weak_source = new WeakReference (source);
			return source;
		}

		protected virtual ISourceBuffer ReadSource (out int start_row, out int end_row,
							    out ArrayList addresses)
		{
			start_row = end_row = 0;
			addresses = null;

			if (disassembler == null)
				return null;

			Console.WriteLine ("READ SOURCE: {0}", this);

			ITargetLocation current = (ITargetLocation) start.Clone ();

			addresses = new ArrayList ();

			StringBuilder sb = new StringBuilder ();

			while (current.Address < end.Address) {
				long address = current.Address;

				IMethod method;
				if ((disassembler.SymbolTable != null) &&
				    disassembler.SymbolTable.Lookup (current, out method) &&
				    (method.StartAddress.Address == current.Address)) {
					if (end_row > 0) {
						sb.Append ("\n");
						end_row++;
					} else
						start_row++;
					sb.Append (String.Format ("{0}:\n",  method.Name));
					end_row++;
				}

				string insn = disassembler.DisassembleInstruction (ref current);
				string line = String.Format ("  {0:x}   {1}\n", address, insn);

				addresses.Add (new LineEntry (address, ++end_row));
				sb.Append (line);
			}

			string method_name = start.ToString ();
			return new SourceBuffer (method_name, sb.ToString ());
		}

		//
		// IMethod
		//

		public string Name {
			get {
				return name;
			}
		}

		public string ImageFile {
			get {
				return image_file;
			}
		}

		public object MethodHandle {
			get {
				return this;
			}
		}

		public IMethodSource Source {
			get {
				return this;
			}
		}

		public ISourceBuffer SourceBuffer {
			get {
				return ReadSource ();
			}
		}

		public int StartRow {
			get {
				return start_row + 1;
			}
		}

		public int EndRow {
			get {
				return end_row;
			}
		}

		public bool IsInSameMethod (ITargetLocation target)
		{
			if ((target.Address < start.Address) || (target.Address >= end.Address))
				return false;

			return true;
		}

		public ISourceLocation Lookup (ITargetLocation target)
		{
			if (!IsInSameMethod (target))
				return null;

			ISourceBuffer source = ReadSource ();
			if (source == null)
				return null;

			for (int i = addresses.Count-1; i >= 0; i--) {
				LineEntry entry = (LineEntry) addresses [i];

				if (entry.Address > target.Address)
					continue;

				return new SourceLocation (source, entry.Line);
			}

			return null;
		}

		public ITargetLocation StartAddress {
			get {
				return (ITargetLocation) start.Clone ();
			}
		}

		public ITargetLocation EndAddress {
			get {
				return (ITargetLocation) end.Clone ();
			}
		}

		public override string ToString ()
		{
			return String.Format ("NativeMethod({0},{1},{2})", name, start, end);
		}
	}
}
