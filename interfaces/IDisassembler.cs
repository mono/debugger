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
		//   If @imethod is non-null, it specifies the current method which will
		//   be used to lookup function names from trampoline calls.
		// </summary>
		AssemblerLine DisassembleInstruction (IMethod method, TargetAddress address);

		// <summary>
		//   The symbol table the disassembler uses to display symbols.
		// </summary>
		ISimpleSymbolTable SymbolTable {
			get; set;
		}
	}
}
