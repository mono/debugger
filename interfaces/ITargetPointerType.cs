namespace Mono.Debugger.Languages
{
	public interface ITargetPointerType : ITargetType
	{
		// <summary>
		//   Whether this is a type-safe pointer.  Type-safe pointers can only
		//   be dereferenced to their current type which non type-safe pointers
		//   can be dereferenced as any type.
		// </summary>
		bool IsTypesafe {
			get;
		}

		// <summary>
		//   Whether this pointer has a static type.  If false, then the type of
		//   the object this pointer points to can only be determined at runtime.
		// </summary>
		bool HasStaticType {
			get;
		}

		ITargetType StaticType {
			get;
		}

		bool IsArray {
			get;
		}
	}
}
