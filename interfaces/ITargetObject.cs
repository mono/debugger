namespace Mono.Debugger.Languages
{
	public interface ITargetObject
	{
		// <summary>
		//   The type info of this object.
		// </summary>
		ITargetType Type {
			get;
		}

		string TypeName {
			get;
		}

		TargetObjectKind Kind {
			get;
		}

		bool IsNull {
			get;
		}

		string Print (ITargetAccess target);
	}
}
