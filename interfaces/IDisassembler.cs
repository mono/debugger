using System;

namespace Mono.Debugger
{
	public interface IDisassembler
	{
		// <summary>
		//   Disassemble one instruction and increment the location.
		// </summary>
		string DisassembleInstruction (ref ITargetLocation location);

		// <summary>
		//   Get the size of the current instruction.
		// </summary>
		int GetInstructionSize (ITargetLocation location);
	}
}
