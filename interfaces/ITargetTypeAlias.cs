namespace Mono.Debugger.Languages
{
	// <summary>
	//   An alias to another type, ie. a typedef.
	// </summary>
	public interface ITargetTypeAlias : ITargetType
	{
		ITargetType TargetType {
			get;
		}
	}
}
