namespace Mono.Debugger.Languages
{
	public interface ITargetFunctionObject : ITargetObject
	{
		ITargetFunctionType Type {
			get;
		}

		ITargetObject Invoke (ITargetObject[] args, bool debug);
	}
}
