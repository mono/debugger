using System;

namespace Mono.Debugger
{
	public interface ITargetStructObject : ITargetObject
	{
		ITargetStructType Type {
			get;
		}

		ITargetObject GetField (int index);

		ITargetObject GetProperty (int index);

		ITargetFunctionObject GetMethod (int index);

		// <summary>
		//   Calls a function in the target to get a textual representation
		//   of the object.  For CIL applications, this'll call Object.ToString().
		// </summary>
		string PrintObject ();
	}
}
