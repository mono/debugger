using System;

namespace Mono.Debugger.Interface
{
	public interface ITargetArrayObject : ITargetObject
	{
		new ITargetArrayType Type {
			get;
		}

		int GetLowerBound (IThread target, int dimension);

		int GetUpperBound (IThread target, int dimension);

		ITargetObject GetElement (IThread target, int[] indices);

		void SetElement (IThread target, int[] indices, ITargetObject obj);
	}
}

