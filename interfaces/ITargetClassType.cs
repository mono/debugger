namespace Mono.Debugger.Languages
{
	public interface ITargetClassType : ITargetStructType
	{
		bool HasParent {
			get;
		}

		ITargetClassType ParentType {
			get;
		}
	}
}
