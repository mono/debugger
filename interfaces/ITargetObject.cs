namespace Mono.Debugger.Languages
{
	public interface ITargetObject
	{
		// <summary>
		//   The type of this object.
		// </summary>
		ITargetTypeInfo Type {
			get;
		}

		// <summary>
		//   If false, then the object can not be accessed because its location is
		//   invalid or the object is corrupted.
		// </summary>
		bool IsValid {
			get;
		}

		// <summary>
		//   Returns the raw contents of this object.  For objects with dynamic size,
		//   this returns a buffer of `Type.Size' bytes which is just enough to get
		//   the object's actual size.  It will not return any dynamic data to avoid
		//   any problems if the object is corrupted and it cannot correctly determine
		//   its size.
		// </summary>
		byte[] RawContents {
			get;
		}

		// <summary>
		//   Returns the size of the object's dynamic content.
		// </summary>
		long DynamicSize {
			get;
		}

		// <summary>
		//   Returns the raw dynamic contents of this object, but not more than
		//   @max_size bytes.  May only be called for object with dynamic size.
		// </summary>
		byte[] GetRawDynamicContents (int max_size);

		TargetLocation Location {
			get;
		}

		string Print ();
	}
}
