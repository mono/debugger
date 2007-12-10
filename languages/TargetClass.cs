namespace Mono.Debugger.Languages
{
	public abstract class TargetClass : DebuggerMarshalByRefObject
	{
		public abstract TargetClassType Type {
			get;
		}

		public abstract bool HasParent {
			get;
		}

		public abstract TargetClass GetParent (Thread thread);

		public abstract TargetFieldInfo[] GetFields (Thread thread);

		public abstract TargetObject GetField (Thread thread,
						       TargetStructObject instance,
						       TargetFieldInfo field);

		public abstract void SetField (Thread thread, TargetStructObject instance,
					       TargetFieldInfo field, TargetObject value);
	}
}
