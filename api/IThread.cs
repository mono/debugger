using System;
using System.Threading;

namespace Mono.Debugger.Interface
{
	[Serializable]
	public delegate object TargetAccessDelegate (IThread target, object user_data);

	public interface IThread
	{
		WaitHandle WaitHandle {
			get;
		}

		TargetState State {
			get;
		}

		int ID {
			get;
		}

		string Name {
			get;
		}

		long TID {
			get;
		}

		IProcess Process {
			get;
		}

		bool IsSystemThread {
			get;
		}

		IStackFrame CurrentFrame {
			get;
		}

		TargetAddress CurrentFrameAddress {
			get;
		}

		IBacktrace GetBacktrace (int max_frames);

		IRegisters GetRegisters ();

		void SetRegisters (IRegisters registers);

		void StepInstruction ();

		void StepNativeInstruction ();

		void NextInstruction ();

		void StepLine ();

		void NextLine ();

		void Finish (bool native);

		void Continue ();

		void Continue (TargetAddress until);

#region Blocking
		ICommandResult RuntimeInvoke (ITargetFunctionType function,
					      ITargetClassObject object_argument,
					      ITargetObject[] param_objects,
					      bool is_virtual);
#endregion

		void Kill ();

		void Detach ();

		void Stop ();

		void Wait ();

		bool HasTarget {
			get;
		}

		bool CanRun {
			get;
		}

		bool IsStopped {
			get;
		}
	}

	public interface ICommandResult
	{
		object Result {
			get;
		}

		WaitHandle CompletedEvent {
			get;
		}

		void Wait ();
	}
}
