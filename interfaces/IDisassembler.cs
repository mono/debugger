using System;

namespace Mono.Debugger
{
	public interface IDisassembler
	{
		// <summary>
		//   Disassemble one instruction and increment the location.
		// </summary>
		string DisassembleInstruction (ref TargetAddress address);

		// <summary>
		//   Get the size of the current instruction.
		// </summary>
		int GetInstructionSize (TargetAddress address);

		// <summary>
		//   Disassemble one method.
		// </summary>
		AssemblerMethod DisassembleMethod (IMethod method);

		AssemblerMethod DisassembleInstruction (TargetAddress address);

		// <summary>
		//   The symbol table the disassembler uses to display symbols.
		// </summary>
		ISymbolTable SymbolTable {
			get; set;
		}
	}
}
