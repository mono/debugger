namespace Mono.Debugger.Languages
{
	public interface ITargetFunctionObject : ITargetObject
	{
		new ITargetFunctionType Type {
			get;
		}
	}
}
