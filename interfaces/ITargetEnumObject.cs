namespace Mono.Debugger.Languages
{
	public interface ITargetEnumObject : ITargetObject
	{
		new ITargetEnumType Type {
			get ;
		}

		ITargetObject Value {
			get ;
		}
	}
}
