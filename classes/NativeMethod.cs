using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
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

		ISourceBuffer read_source ()
		{
			if (disassembler == null)
				return null;

			Console.WriteLine ("READ SOURCE: {0}", this);

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
					} else {
						start_row++;
						addresses.Add (address);
					}
					sb.Append (String.Format ("{0}:\n",  method.Name));
					end_row++;
				}

				string insn = disassembler.DisassembleInstruction (ref current);
				string line = String.Format ("  {0:x}   {1}\n", address, insn);

				addresses.Add (address);
				sb.Append (line);
				end_row++;
			}

			string method_name = start.ToString ();
			source = new SourceBuffer (method_name, sb.ToString ());
			weak_source = new WeakReference (source);
			return source;
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
				return read_source ();
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

			ISourceBuffer source = read_source ();
			if (source == null)
				return null;

			for (int i = start_row; i < end_row; i++)
				if ((long) addresses [i] >= target.Address)
					return new SourceLocation (source, i + 1);

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
