using System;

namespace Mono.Debugger.Interface
{
	public interface ITargetVariable
	{
		string Name {
			get;
		}

		ITargetType Type {
			get;
		}

		bool IsInScope (TargetAddress address);

		ITargetObject GetObject (IStackFrame frame);

		bool CanWrite {
			get;
		}

		void SetObject (IStackFrame frame, ITargetObject obj);
	}
}
