using System;

namespace Mono.Debugger
{
	internal abstract class Disassembler
	{
		// <summary>
		//   Get the size of the current instruction.
		// </summary>
		public abstract int GetInstructionSize (TargetAddress address);

		// <summary>
		//   Disassemble one method.
		// </summary>
		public abstract AssemblerMethod DisassembleMethod (Method method);

		// <summary>
		//   Disassemble one instruction.
		//   If @imethod is non-null, it specifies the current method which will
		//   be used to lookup function names from trampoline calls.
		// </summary>
		public abstract AssemblerLine DisassembleInstruction (Method method,
								      TargetAddress address);

		// <summary>
		//   The symbol table the disassembler uses to display symbols.
		// </summary>
		internal abstract ISimpleSymbolTable SymbolTable {
			get; set;
		}
	}
}
