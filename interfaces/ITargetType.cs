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

		ILanguage Language {
			get;
		}

		// <summary>
		//   Whether an instance of this type has a fixed size.
		// </summary>
		bool HasFixedSize {
			get;
		}

		// <summary>
		//   The size of an instance of this type or - if HasFixedSize
		//   is false - the minimum number of bytes which must be read
		//   to determine its size.
		// </summary>
		int Size {
			get;
		}
	}
}
