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

		void RuntimeInvoke (TargetFunctionType method_argument,
				    TargetObject object_argument,
				    TargetObject[] param_objects);

		TargetObject RuntimeInvoke (TargetFunctionType method_argument,
					     TargetObject object_argument,
					     TargetObject[] param_objects,
					     out string exc_message);

		object Invoke (TargetAccessDelegate func, object user_data);

		AssemblerLine DisassembleInstruction (IMethod method, TargetAddress address);

		AssemblerMethod DisassembleMethod (IMethod method);
	}
}
