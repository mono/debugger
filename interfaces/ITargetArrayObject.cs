namespace Mono.Debugger.Languages
{
	public interface ITargetArrayObject : ITargetObject
	{
		int LowerBound {
			get;
		}

		int UpperBound {
			get;
		}

		ITargetObject this [int index] {
			get;
		}
	}
}
