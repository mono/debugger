using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public class NativeMethod : MethodBase
	{
		IDisassembler disassembler;
		IMethod method;

		public NativeMethod (IDisassembler dis, IMethod method)
			: base (method)
		{
			this.disassembler = dis;
			this.method = method;
		}

		public override object MethodHandle {
			get {
				return this;
			}
		}

		protected override ISourceBuffer ReadSource (out int start_row, out int end_row,
							     out ArrayList addresses)
		{
			start_row = end_row = 0;
			addresses = null;

			if (disassembler == null)
				return null;

			ITargetLocation current = method.StartAddress;

			addresses = new ArrayList ();

			StringBuilder sb = new StringBuilder ();

			long end_address = method.EndAddress.Address;

			while (current.Address < end_address) {
				long address = current.Address;

				IMethod method = null;
				if (disassembler.SymbolTable != null)
					method = disassembler.SymbolTable.Lookup (current);
				if ((method != null) && (method.StartAddress.Address == current.Address)) {
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

			return new SourceBuffer (method.Name, sb.ToString ());
		}
	}
}
