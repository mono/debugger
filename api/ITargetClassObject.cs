namespace Mono.Debugger.Interface
{
	public interface ITargetClassObject : ITargetObject
	{
		new ITargetClassType Type {
			get;
		}

		ITargetClassObject GetParentObject (IThread target);

		ITargetClassObject GetCurrentObject (IThread target);

		ITargetObject GetField (IThread target, ITargetFieldInfo field);

		void SetField (IThread target, ITargetFieldInfo field, ITargetObject obj);
	}
}
