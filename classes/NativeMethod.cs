using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public class NativeMethod : MethodBase, IMethodSource, IMethod
	{
		IDisassembler disassembler;

		public NativeMethod (IDisassembler dis, IMethod method)
			: base (method)
		{
			this.disassembler = dis;
		}

		public NativeMethod (IDisassembler dis, string name, string image_file,
				     ITargetLocation start, ITargetLocation end)
			: this (name, image_file, start, end)
		{
			this.disassembler = dis;
		}

		public NativeMethod (string name, string image_file,
				     ITargetLocation start, ITargetLocation end)
			: base (name, image_file, start.Address, end.Address)
		{ }

		protected override ISourceBuffer ReadSource (out int start_row, out int end_row,
							     out ArrayList addresses)
		{
			start_row = end_row = 0;
			addresses = null;

			if (disassembler == null)
				return null;

			Console.WriteLine ("READ SOURCE: {0}", this);

			ITargetLocation current = StartAddress;

			addresses = new ArrayList ();

			StringBuilder sb = new StringBuilder ();

			long end_address = EndAddress.Address;

			while (current.Address < end_address) {
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

			return new SourceBuffer (Name, sb.ToString ());
		}

		//
		// IMethod
		//

		public override object MethodHandle {
			get {
				return this;
			}
		}
	}
}
