namespace Mono.Debugger.Languages
{
	public interface ITargetTypeInfo
	{
		ITargetType Type {
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

		ITargetObject GetObject (TargetLocation location);
	}
}
