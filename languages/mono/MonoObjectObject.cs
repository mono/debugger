using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoObjectObject : TargetObjectObject
	{
		public new readonly MonoObjectType Type;

		public MonoObjectObject (MonoObjectType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		internal override TargetClassObject GetClassObject (TargetMemoryAccess target)
		{
			return (TargetClassObject) Type.ClassType.GetObject (target, Location);
		}

		internal override TargetType GetCurrentType (TargetMemoryAccess target)
		{
			// location.Address resolves to the address of the MonoObject,
			// dereferencing it once gives us the vtable, dereferencing it
			// twice the class.
			TargetAddress address;
			address = target.ReadAddress (Location.GetAddress (target));
			address = target.ReadAddress (address);

			return MonoRuntime.ReadMonoClass (Type.File.MonoLanguage, target, address);
		}

		internal override TargetObject GetDereferencedObject (TargetMemoryAccess target)
		{
			TargetType current_type = GetCurrentType (target);
			if (current_type == null)
				return null;

			// If this is a reference type, then the `MonoObject *' already
			// points to the boxed object itself.
			// If it's a valuetype, then the boxed contents is immediately
			// after the `MonoObject' header.

			int offset = current_type.IsByRef ? 0 : type.Size;
			TargetLocation new_location = Location.GetLocationAtOffset (offset);
			TargetObject obj = current_type.GetObject (target, new_location);
			return obj;
		}
	}
}
