namespace Mono.Debugger.Interface
{
	public interface ITargetPointerObject : ITargetObject
	{
		new ITargetPointerType Type {
			get;
		}

		ITargetType GetCurrentType (IThread target);

		ITargetObject GetDereferencedObject (IThread target);

		ITargetObject GetArrayElement (IThread target, int index);
	}
}
