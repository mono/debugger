namespace Mono.Debugger.Languages
{
	public interface ITargetStructObject : ITargetObject
	{
		new ITargetStructType Type {
			get;
		}

		ITargetObject GetField (int index);

		void SetField (int index, ITargetObject obj);
	}
}
