namespace Mono.Debugger.Languages
{
	public interface ITargetClassObject : ITargetStructObject
	{
		new ITargetClassType Type {
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
