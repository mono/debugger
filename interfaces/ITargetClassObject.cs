namespace Mono.Debugger.Languages
{
	public interface ITargetClassObject : ITargetStructObject
	{
		ITargetClassType Type {
			get;
		}

		ITargetClassObject Parent {
			get;
		}
	}
}
