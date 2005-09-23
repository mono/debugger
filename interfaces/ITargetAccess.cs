using System;

using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	public delegate object TargetAccessDelegate (ITargetAccess target, object user_data);

	public interface ITargetAccess
	{
		int ID {
			get;
		}

		string Name {
			get;
		}

		ITargetMemoryInfo TargetMemoryInfo {
			get;
		}

		ITargetInfo TargetInfo {
			get;
		}

		ITargetMemoryAccess TargetMemoryAccess {
			get;
		}

		TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
					  TargetAddress arg2);

		TargetAddress CallMethod (TargetAddress method, long method_argument,
					  string string_argument);

		void RuntimeInvoke (ITargetFunctionType method_argument,
				    ITargetObject object_argument,
				    ITargetObject[] param_objects);

		ITargetObject RuntimeInvoke (ITargetFunctionType method_argument,
					     ITargetObject object_argument,
					     ITargetObject[] param_objects,
					     out string exc_message);

		object Invoke (TargetAccessDelegate func, object user_data);

		AssemblerLine DisassembleInstruction (IMethod method, TargetAddress address);

		AssemblerMethod DisassembleMethod (IMethod method);
	}
}
