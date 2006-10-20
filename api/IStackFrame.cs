using System;

namespace Mono.Debugger.Interface
{
	public interface IRegister
	{
		string Name {
			get;
		}

		int Index {
			get;
		}

		bool IsValid {
			get;
		}

		long Value {
			get; set;
		}
	}

	public interface IRegisters : ICollection
	{
		IRegister this [int index] {
			get;
		}

		IRegister this [string name] {
			get;
		}

		bool FromCurrentFrame {
			get;
		}
	}

	public interface IStackFrame
	{
		int Level {
			get;
		}

		TargetAddress TargetAddress {
			get;
		}

		TargetAddress StackPointer {
			get;
		}

		TargetAddress FrameAddress {
			get;
		}

		IThread Thread {
			get;
		}

		IRegisters Registers {
			get;
		}

		IMethod Method {
			get;
		}

		ITargetObject ExceptionObject {
			get;
		}
	}
}
