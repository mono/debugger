using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public class NativeMethod : MethodBase
	{
		protected IDisassembler disassembler;
		NativeMethodSource source;
		IMethod method;

		public NativeMethod (IDisassembler dis, IMethod method)
			: base (method)
		{
			this.disassembler = dis;
			this.method = method;

			SetSource (new NativeMethodSource (this));
		}

		public override object MethodHandle {
			get {
				return this;
			}
		}

		public override IVariable[] Parameters {
			get {
				throw new NotSupportedException ();
			}
		}

		public override IVariable[] Locals {
			get {
				throw new NotSupportedException ();
			}
		}

		private class NativeMethodSource : MethodSource
		{
			NativeMethod method;

			public NativeMethodSource (NativeMethod method)
				: base (method)
			{
				this.method = method;
			}

			protected override ISourceBuffer ReadSource (out int start_row, out int end_row,
								     out ArrayList addresses)
			{
				start_row = end_row = 0;
				addresses = null;

				if (method.disassembler == null)
					return null;

				TargetAddress current = method.StartAddress;

				addresses = new ArrayList ();

				StringBuilder sb = new StringBuilder ();

				TargetAddress end_address = method.EndAddress;

				while (current < end_address) {
					IMethod imethod = null;
					if (method.disassembler.SymbolTable != null)
						imethod = method.disassembler.SymbolTable.Lookup (current);
					if ((imethod != null) && (imethod.StartAddress == current)) {
						if (end_row > 0) {
							sb.Append ("\n");
							end_row++;
						} else
							start_row++;
						sb.Append (String.Format ("{0}:\n",  imethod.Name));
						end_row++;
					}

					TargetAddress address = current;
					string insn = method.disassembler.DisassembleInstruction (ref current);
					string line = String.Format ("  {0:x}   {1}\n", address, insn);

					addresses.Add (new LineEntry (address, ++end_row));
					sb.Append (line);
				}

				return new SourceBuffer (method.Name, sb.ToString ());
			}
		}
	}
}
