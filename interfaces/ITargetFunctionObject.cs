namespace Mono.Debugger.Languages
{
	public interface ITargetFunctionObject : ITargetObject
	{
		ITargetFunctionType Type {
			get;
		}

		ITargetObject Invoke (object[] args, bool debug);
	}
}
