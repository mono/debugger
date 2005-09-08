namespace Mono.Debugger.Languages
{
	public interface ITargetArrayObject : ITargetObject
	{
		new ITargetArrayType Type {
			get;
		}

		int GetLowerBound (int dimension);

		int GetUpperBound (int dimension);

		ITargetObject this [int[] indices] {
			get; set;
		}
	}
}
