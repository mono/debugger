namespace Mono.Debugger.Languages
{
	public interface ITargetClassObject : ITargetStructObject
	{
		ITargetClassType Type {
			get;
		}

		ITargetClassObject CurrentObject {
			get;
		}

		ITargetClassObject Parent {
			get;
		}
	}
}
