namespace Mono.Debugger.Languages
{
	public interface ITargetFunctionObject : ITargetObject
	{
		new ITargetFunctionType Type {
			get;
		}

		ITargetObject Invoke (ITargetAccess target, ITargetObject instance,
				      ITargetObject[] args, bool debug);
	}
}
