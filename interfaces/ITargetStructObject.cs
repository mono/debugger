namespace Mono.Debugger.Languages
{
	public interface ITargetStructObject : ITargetObject
	{
		new ITargetStructType Type {
			get;
		}

		ITargetObject GetField (int index);

		ITargetObject GetProperty (int index);

		ITargetObject GetEvent (int index);

		ITargetFunctionObject GetMethod (int index);

		// <summary>
		//   Calls a function in the target to get a textual representation
		//   of the object.  For CIL applications, this'll call Object.ToString().
		// </summary>
		string PrintObject ();
	}
}
