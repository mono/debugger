using System;

namespace Mono.Debugger
{
	public interface ITargetPointerObject : ITargetObject
	{
		ITargetPointerType Type {
			get;
		}

		// <summary>
		//   The current type of the object pointed to by this pointer.
		//   May only be used if ITargetPointerType.HasStaticType is false.
		// </summary>
		ITargetType CurrentType {
			get;
		}

		// <summary>
		//   If true, this pointer can be dereferenced to an ITargetObject.
		// </summary>
		bool HasDereferencedObject {
			get;
		}

		// <summary>
		//   If HasDereferencedObject is true, return the dereferenced object.
		// </summary>
		ITargetObject DereferencedObject {
			get;
		}

		// <summary>
		//   Dereference the pointer and read @size bytes from the location it
		//   points to.  Only allowed for non-typesafe pointers.
		// </summary>
		byte[] GetDereferencedContents (int size);

		bool HasAddress {
			get;
		}

		TargetAddress Address {
			get;
		}
	}
}
