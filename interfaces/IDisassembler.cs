using System;

namespace Mono.Debugger
{
	public interface IDisassembler
	{
		// <summary>
		//   Get the size of the current instruction.
		// </summary>
		int GetInstructionSize (TargetAddress address);

		// <summary>
		//   Disassemble one method.
		// </summary>
		AssemblerMethod DisassembleMethod (IMethod method);

		// <summary>
		//   Disassemble one instruction.
		// </summary>
		AssemblerLine DisassembleInstruction (TargetAddress address);

		// <summary>
		//   The symbol table the disassembler uses to display symbols.
		// </summary>
		ISymbolTable SymbolTable {
			get; set;
		}
	}
}
