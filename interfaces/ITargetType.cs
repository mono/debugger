namespace Mono.Debugger.Languages
{
	// <summary>
	//   This interface denotes a type in the target application.
	// </summary>
	public interface ITargetType
	{
		string Name {
			get;
		}

		TargetObjectKind Kind {
			get;
		}

		object TypeHandle {
			get;
		}

		ITargetTypeInfo Resolve ();
	}
}
