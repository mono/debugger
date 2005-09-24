namespace Mono.Debugger.Languages
{
	public interface ITargetArrayObject : ITargetObject
	{
		new ITargetArrayType Type {
			get;
		}

		int GetLowerBound (int dimension);

		int GetUpperBound (int dimension);

		ITargetObject GetElement (ITargetAccess target, int[] indices);

		void SetElement (ITargetAccess target, int[] indices, ITargetObject obj);
	}
}
